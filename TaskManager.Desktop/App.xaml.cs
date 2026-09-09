using System.IO;
using System.Threading;
using System.Net.Http;
using System.Windows;
using System.Windows.Interop;
using TaskManager.Core;
using TaskManager.Core.Data;
using TaskManager.Core.Services;
using TaskManager.Desktop.Services;

namespace TaskManager.Desktop;

/// <summary>
/// Arranque. La aplicacion no tiene ventana principal: vive en la bandeja y despliega el panel
/// rapido cuando se le llama (especificacion 6).
/// </summary>
public partial class App : Application
{
    private LocalDatabase _database = null!;
    private SettingsService _settings = null!;
    private TaskService _tasks = null!;
    private TrayIconHost _tray = null!;
    private FlyoutWindow _flyout = null!;
    private GlobalHotkey? _hotkey;
    private HttpClient? _http;
    private SupabaseAuthService _auth = null!;
    private ISyncService _sync = null!;
    private SyncCoordinator? _syncing;
    private CalendarWindow? _calendar;
    private MainWindow? _main;
    private MailOAuthService _mailOAuth = null!;
    private IMailReader _mail = null!;
    private ReminderScheduler? _reminders;

    /// <summary>
    /// Una sola aplicacion a la vez, y abrirla otra vez enseña la que ya esta.
    /// </summary>
    /// <remarks>
    /// <para>Es una aplicacion de bandeja: la segunda no se ve por ninguna parte —arranca escondida
    /// como la primera— asi que lo unico que notaba el usuario era un icono de mas en la bandeja y,
    /// peor, dos procesos escribiendo en la misma base de SQLite. Pulsar el acceso directo cuando ya
    /// esta abierta tiene que hacer lo que se espera: traer su ventana al frente.</para>
    ///
    /// <para>El nombre lleva el usuario dentro: con dos sesiones de Windows abiertas, cada una tiene
    /// su aplicacion y su base de datos, y compartir el aviso las mezclaria.</para>
    /// </remarks>
    private static readonly string NombreUnica = $"Socratic.TaskManager.unica.{Environment.UserName}";
    private static readonly string NombreAviso = $"Socratic.TaskManager.abrir.{Environment.UserName}";

    private static Mutex? _unica;
    private static EventWaitHandle? _aviso;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!TomarLaVez())
        {
            return;
        }

        ThemeManager.Apply();

        // Que Windows sepa abrir los enlaces de invitacion a un grupo. Se rehace en cada arranque
        // porque apunta al .exe, y este se entrega copiandolo a mano.
        GroupLinkProtocol.Registrar();

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Socratic", "TaskManager");
        Directory.CreateDirectory(folder);

        _database = new LocalDatabase(Path.Combine(folder, "taskmanager.db3"));

        // Los ajustes van antes que el repositorio: de ahi sale la cuenta que esta dentro, y con
        // ella sabe el repositorio de quien es lo que lee y lo que escribe.
        _settings = new SettingsService(_database);
        await _settings.LoadAsync();

        var repository = new TaskRepository(_database, _settings);

        // El idioma se resuelve antes de crear ninguna ventana: los textos del XAML se fijan al
        // construirla, asi que leerlo despues dejaria la primera ventana en el idioma equivocado.
        Localization.Loc.Use(new LocalizationService(_settings));

        // HandyControl trae sus 40 cadenas en chino y sin ningun otro idioma dentro; aqui se le
        // cambian por las nuestras. Va antes de crear ninguna ventana, que es cuando se leen.
        Localization.HandyControlLang.Install();

        // Y lo que pinta WPF por su cuenta —los nombres de los dias del calendario que despliega
        // cada selector de fecha— tambien tiene que salir en el idioma de la aplicacion.
        Localization.WpfCulture.Install();

        // El desglose intenta primero el modelo local y cae a plantillas: nunca se queda sin pasos.
        // El modelo local no se configura: se busca donde escuchan por costumbre (Ollama y
        // LM Studio en el propio equipo). Si no hay ninguno, el desglose cae a plantillas y
        // funciona igual.
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var breakdown = new CascadingBreakdownService(
            new LocalLlmBreakdownService(_http, () => "http://localhost:11434", () => _settings.LlmModel),
            new LocalLlmBreakdownService(_http, () => "http://localhost:1234", () => _settings.LlmModel),
            new HeuristicBreakdownService());

        _tasks = new TaskService(repository, _settings, breakdown);
        await _tasks.InitializeAsync();

        // Entrada con Google o con Microsoft: navegador del sistema + servidor local de un solo
        // uso, y los tokens
        // cifrados con DPAPI. La sesion se recupera sola mientras el refresco siga siendo valido.
        _auth = new SupabaseAuthService(_http, _settings, new DpapiTokenStore(folder), new LoopbackOAuthBrowser());
        await _auth.RestoreSessionAsync();

        // La entrada es obligatoria: sin cuenta no se monta la bandeja. Se pregunta aqui, antes de
        // crear ninguna ventana, porque todo lo que viene detras atribuye lo que pasa a un usuario
        // y el nombre de la cuenta es el nombre que enseña la aplicacion.
        if (!await EnsureSignedInAsync())
        {
            return;
        }

        // Correo: registro de Entra, navegador del sistema y un servidor local de un solo uso
        // para recoger la respuesta.
        _mailOAuth = new MailOAuthService(_http, new LoopbackOAuthBrowser(), new DpapiTokenStore(folder));
        _mail = new MailKitReader();

        // Sincronizacion con el movil. El coordinador decide cuando (al entrar, al volver, tras
        // cada cambio y cada pocos minutos); aqui solo se le dice que arranque, sin esperarlo: si
        // la red va lenta o no hay, el panel tiene que abrirse igual de rapido.
        _sync = SupabaseConfig.IsConfigured
            ? new SupabaseSyncService(_http, repository, _settings, _auth)
            : new LocalOnlySyncService(repository);

        _syncing = new SyncCoordinator(_sync, _auth, repository, _settings);

        _flyout = new FlyoutWindow(_tasks, _settings, _syncing) { Icon = TrayIconHost.CreateWindowIcon() };
        _flyout.PendingChanged += (_, pending) => _tray.SetPending(pending);
        _flyout.SettingsRequested += (_, _) => OpenSettings();
        _flyout.AboutRequested += (_, _) => OpenAbout();
        _flyout.CalendarRequested += (_, _) => OpenCalendar();
        _flyout.MainRequested += (_, _) => OpenMain();

        _tray = new TrayIconHost();
        _tray.Activated += (_, _) => _flyout.ShowFlyout();
        _tray.SettingsRequested += (_, _) => OpenSettings();
        _tray.MainRequested += (_, _) => OpenMain();
        _tray.ExitRequested += (_, _) => Shutdown();

        // El atajo global necesita un handle: se fuerza sin llegar a mostrar la ventana.
        var handle = new WindowInteropHelper(_flyout).EnsureHandle();
        _hotkey = new GlobalHotkey(handle);
        _hotkey.Pressed += (_, _) => _flyout.ShowFlyout();

        var combination = _settings.Get(SettingsService.KeyHotkey, "Ctrl+Alt+T");
        if (!_hotkey.Register(combination))
        {
            _tray.Notify("Task Manager", Localization.Loc.Format("HotkeyTaken", combination));
        }

        // Una tarea creada en el movil se anuncia aqui en cuanto baja, y el panel se relee solo.
        _syncing.TaskArrived += (_, task) =>
            Dispatcher.Invoke(() => _tray.Notify(Localization.Loc.Get("MenuMyTasks"),
                Localization.Loc.Format("TaskArrivedFromDevice", task.Title)));

        _syncing.Changed += (_, _) => Dispatcher.Invoke(async () =>
        {
            await _flyout.ReloadAsync();
            _tray.SetPending(await repository.CountPendingAsync());
        });

        _syncing.Start();

        _tray.SetPending(await repository.CountPendingAsync());

        // Recordatorios: el aviso diario y el de las tareas que vencen hoy, como globo de bandeja.
        _reminders = new ReminderScheduler(repository, _settings, _tray);

        // Siempre arranca en la bandeja, se abra como se abra: es una aplicacion de bandeja, y
        // plantar el panel en pantalla al encender el equipo estorba mas que ayuda. Se despliega
        // con el clic en el icono o con el atajo global.
        _tray.Notify("Task Manager", Localization.Loc.Format("TrayRunning",
            _settings.Get(SettingsService.KeyHotkey, "Ctrl+Alt+T")));

        // Abierta desde un enlace de invitacion: se enseña la ventana y se entra en el grupo.
        if (GroupLinkProtocol.EnLosArgumentos(e.Args) is not null || File.Exists(GroupLinkProtocol.BuzonPath))
        {
            if (GroupLinkProtocol.EnLosArgumentos(e.Args) is { } invite)
            {
                GroupLinkProtocol.Dejar(invite);
            }

            OpenMain();
            _main?.AtenderInvitacion();
        }
    }

    /// <summary>
    /// Se queda con el turno, o le pide a la que ya esta abierta que se enseñe y se apaga.
    /// </summary>
    /// <returns><c>false</c> si ya habia otra: entonces no hay nada mas que hacer aqui.</returns>
    private bool TomarLaVez()
    {
        _unica = new Mutex(true, NombreUnica, out var primera);

        if (!primera)
        {
            // Si venia con una invitacion (taskmanager://join?...), se la deja escrita a la que ya
            // esta corriendo: esta copia se apaga en un segundo y con ella se irian los argumentos.
            if (GroupLinkProtocol.EnLosArgumentos(Environment.GetCommandLineArgs()) is { } invite)
            {
                GroupLinkProtocol.Dejar(invite);
            }

            try
            {
                EventWaitHandle.OpenExisting(NombreAviso).Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // La otra esta arrancando todavia y aun no ha creado el aviso. Se pierde el «abre la
                // ventana», que es lo de menos: la aplicacion se esta abriendo igual.
            }

            Shutdown();
            return false;
        }

        // El hilo que espera el aviso es de fondo: no impide que la aplicacion se cierre.
        _aviso = new EventWaitHandle(false, EventResetMode.AutoReset, NombreAviso);

        var espera = new Thread(() =>
        {
            while (_aviso.WaitOne())
            {
                Dispatcher.Invoke(() =>
                {
                    OpenMain();
                    _main?.AtenderInvitacion();
                });
            }
        })
        {
            IsBackground = true,
            Name = "TaskManager: segunda instancia",
        };

        espera.Start();
        return true;
    }

    /// <summary>
    /// Deja pasar solo con cuenta. Devuelve <c>false</c> cuando el usuario cierra la ventana sin
    /// entrar, en cuyo caso la propia ventana ya ha pedido apagar la aplicacion y el arranque no
    /// tiene nada mas que hacer.
    /// </summary>
    /// <remarks>
    /// Lo hecho antes de entrar se traspasa a la cuenta (<see cref="TaskService.AdoptAccountAsync"/>):
    /// una base recien creada no tiene nada que traspasar, pero la que venia de una version sin
    /// entrada obligatoria si, y perder el nivel y las rachas por actualizar seria un castigo.
    /// </remarks>
    private async Task<bool> EnsureSignedInAsync()
    {
        if (_auth.IsSignedIn)
        {
            return true;
        }

        var login = new LoginWindow(_auth) { Icon = TrayIconHost.CreateWindowIcon() };
        login.ShowDialog();

        if (login.User is null)
        {
            return false;
        }

        await _tasks.AdoptAccountAsync(login.User.Id);
        return true;
    }

    /// <summary>
    /// Vuelve a montar la interfaz en el idioma nuevo.
    /// </summary>
    /// <remarks>
    /// Los textos del XAML se fijan al construir la ventana, asi que cambiarlos uno a uno seria
    /// recorrer el arbol entero y acordarse de todos. Recrear el panel y el menu de la bandeja es
    /// menos fino y no se deja nada: es la misma decision que en el movil, donde se reconstruye el
    /// Shell. Se nota poco porque es una accion que se hace una vez.
    /// </remarks>
    public void RebuildUi()
    {
        // El idioma acaba de cambiar: lo que pinta WPF por su cuenta —los calendarios de los
        // selectores de fecha— va por la cultura, no por nuestros textos, y hay que rehacerla.
        Localization.WpfCulture.Install();

        var pending = _tray.Pending;

        _flyout.CloseForReal();
        _flyout = new FlyoutWindow(_tasks, _settings, _syncing) { Icon = TrayIconHost.CreateWindowIcon() };
        _flyout.PendingChanged += (_, count) => _tray.SetPending(count);
        _flyout.SettingsRequested += (_, _) => OpenSettings();
        _flyout.AboutRequested += (_, _) => OpenAbout();
        _flyout.CalendarRequested += (_, _) => OpenCalendar();
        _flyout.MainRequested += (_, _) => OpenMain();

        // El atajo global cuelga de un handle de ventana: al cambiar de ventana hay que rehacerlo.
        var handle = new WindowInteropHelper(_flyout).EnsureHandle();
        _hotkey?.Dispose();
        _hotkey = new GlobalHotkey(handle);
        _hotkey.Pressed += (_, _) => _flyout.ShowFlyout();
        _hotkey.Register(_settings.Get(SettingsService.KeyHotkey, "Ctrl+Alt+T"));

        _tray.RebuildMenu();
        _tray.SetPending(pending);
    }

    /// <summary>
    /// Vuelve a montar la interfaz con las listas de la cuenta que acaba de entrar.
    /// </summary>
    /// <remarks>
    /// <para>Cambiar de cuenta cambia <b>todo</b> lo que se ve: las listas, las tareas, el contador
    /// de la bandeja y el nivel. Las ventanas abiertas siguen enseñando lo de la anterior, y
    /// refrescarlas una a una seria acordarse de todas cada vez que se añada una; se cierran y se
    /// rehacen, que es la misma decision que con el cambio de idioma
    /// (<see cref="RebuildUi"/>).</para>
    ///
    /// <para>La sincronizacion no se pide aqui: el coordinador ya arranca una vuelta al cambiar el
    /// usuario (<c>SyncCoordinator.OnUserChanged</c>), y lo que baje repinta el panel solo.</para>
    /// </remarks>
    public void ReloadForAccount()
    {
        _main?.Close();
        _calendar?.Close();

        RebuildUi();

        _ = Dispatcher.InvokeAsync(async () =>
            _tray.SetPending(await _tasks.Repository.CountPendingAsync()));
    }

    /// <summary>
    /// Abre el calendario, o trae al frente el que ya estuviera abierto.
    /// </summary>
    /// <remarks>
    /// Se guarda la referencia para no acabar con cinco calendarios apilados cuando se pulsa el
    /// boton varias veces, que es lo que pasa si cada clic crea una ventana nueva.
    /// </remarks>
    /// <summary>Abre la ventana principal, o la trae al frente si ya estaba.</summary>
    private void OpenMain()
    {
        if (_main is { IsLoaded: true })
        {
            AlFrente(_main);
            return;
        }

        _main = new MainWindow(_tasks, _settings, _syncing, _sync)
        {
            Icon = TrayIconHost.CreateWindowIcon(),
        };

        _main.Closed += (_, _) => _main = null;
        _main.Show();
        AlFrente(_main);
    }

    /// <summary>
    /// Trae una ventana al frente de verdad.
    /// </summary>
    /// <remarks>
    /// <c>Activate()</c> a secas no basta cuando quien pide el cambio no es la aplicacion que tiene
    /// el foco —que es justo el caso: el clic ha sido en otro proceso, el del acceso directo—.
    /// Windows lo ignora para que ninguna aplicacion pueda robar el foco. Subirla un instante como
    /// <c>Topmost</c> y bajarla es la forma de siempre de saltarselo sin dejarla clavada arriba.
    /// </remarks>
    private static void AlFrente(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private void OpenCalendar()
    {
        if (_calendar is { IsLoaded: true })
        {
            _calendar.Activate();
            return;
        }

        _calendar = new CalendarWindow(_tasks) { Icon = TrayIconHost.CreateWindowIcon() };
        _calendar.Closed += (_, _) => _calendar = null;
        _calendar.Show();
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings, _hotkey, _auth, _tasks)
        {
            Icon = TrayIconHost.CreateWindowIcon(),
        };
        window.ShowDialog();
    }

    /// <summary>«Acerca de»: version, contacto, idioma, privacidad y licencia, como en el movil.</summary>
    private void OpenAbout()
    {
        var window = new AboutWindow(_settings)
        {
            Icon = TrayIconHost.CreateWindowIcon(),
        };
        window.ShowDialog();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _syncing?.Dispose();
        _reminders?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _http?.Dispose();
        base.OnExit(e);
    }
}
