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
    private static string NombreUnica => string.Format(Rutas.Instancia, "unica");
    private static string NombreAviso => string.Format(Rutas.Instancia, "abrir");

    private static Mutex? _unica;
    private static EventWaitHandle? _aviso;

    /// <summary>Con que se habla con el servidor. Las pruebas ponen uno de mentira.</summary>
    internal static Func<HttpClient> CrearHttp { get; set; } =
        () => new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>El panel de la bandeja, el icono y la ventana principal, para quien los necesite mirar.</summary>
    internal FlyoutWindow Panel => _flyout;

    internal TrayIconHost Bandeja => _tray;

    internal MainWindow? Principal => _main;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        await ArrancarAsync(e.Args);
    }

    /// <summary>Todo el arranque, con los argumentos con los que se abrio el programa.</summary>
    internal async Task ArrancarAsync(string[] args)
    {
        InstalarGestorDeErrores();

#if DEBUG
        // Solo en Debug: «--preview-novedades [es|en]» enseña Acerca de y las Novedades con una
        // base temporal, sin cuenta, sin la instancia unica y sin tocar los datos de verdad.
        if (args.Length > 0 && args[0] == "--preview-novedades")
        {
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            ThemeManager.Apply();
            var previewDb = Path.Combine(Path.GetTempPath(), $"taskmanager-preview-{Guid.NewGuid():N}.db3");
            _settings = new SettingsService(new LocalDatabase(previewDb));
            await _settings.LoadAsync();
            await _settings.SetAsync(SettingsService.KeyLanguage, args.Length > 1 ? args[1] : "es");
            Localization.Loc.Use(new LocalizationService(_settings));
            if (_settings.HasUnseenVersion(WhatsNewWindow.CurrentVersion()))
            {
                OpenWhatsNew();
            }

            OpenAbout();

            return;
        }
#endif

        if (!TomarLaVez(args))
        {
            return;
        }

        ThemeManager.Apply();

        // Que Windows sepa abrir los enlaces de invitacion a un grupo. Se rehace en cada arranque
        // porque apunta al .exe, y este se entrega copiandolo a mano.
        GroupLinkProtocol.Registrar();

        var folder = Rutas.Carpeta;
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

        _http = CrearHttp();
        _tasks = new TaskService(repository, _settings);
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

        _flyout = CrearPanel();

        _tray = new TrayIconHost();
        _tray.Activated += (_, _) => _flyout.ShowFlyout();
        _tray.SettingsRequested += (_, _) => OpenSettings();
        _tray.MainRequested += (_, _) => OpenMain();
        _tray.WhatsNewRequested += (_, _) => OpenWhatsNew();
        _tray.ExitRequested += (_, _) => Ventanas.Apagar();

        if (!MontarAtajo())
        {
            _tray.Notify("Task Manager", Localization.Loc.Format("HotkeyTaken",
                _settings.Get(SettingsService.KeyHotkey, "Ctrl+Alt+T")));
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
        if (GroupLinkProtocol.EnLosArgumentos(args) is not null || File.Exists(GroupLinkProtocol.BuzonPath))
        {
            if (GroupLinkProtocol.EnLosArgumentos(args) is { } invite)
            {
                GroupLinkProtocol.Dejar(invite);
            }

            OpenMain();
            _main?.AtenderInvitacion();
        }

        // Version nueva: sus novedades salen solas una vez (General 6.7).
        if (_settings.HasUnseenVersion(WhatsNewWindow.CurrentVersion()))
        {
            OpenWhatsNew();
        }
    }

    /// <summary>
    /// Se queda con el turno, o le pide a la que ya esta abierta que se enseñe y se apaga.
    /// </summary>
    /// <returns><c>false</c> si ya habia otra: entonces no hay nada mas que hacer aqui.</returns>
    private bool TomarLaVez(string[] args)
    {
        _unica = new Mutex(true, NombreUnica, out var primera);

        if (!primera)
        {
            // Si venia con una invitacion (taskmanager://join?...), se la deja escrita a la que ya
            // esta corriendo: esta copia se apaga en un segundo y con ella se irian los argumentos.
            if (GroupLinkProtocol.EnLosArgumentos(args) is { } invite)
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

            Ventanas.Apagar();
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
        Ventanas.Modal(login);

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
        _flyout = CrearPanel();

        // El atajo global cuelga de un handle de ventana: al cambiar de ventana hay que rehacerlo.
        MontarAtajo();

        _tray.RebuildMenu();
        _tray.SetPending(pending);
    }

    /// <summary>El panel rapido, con sus botones enganchados a las ventanas que abren.</summary>
    private FlyoutWindow CrearPanel()
    {
        var flyout = new FlyoutWindow(_tasks, _settings, _syncing) { Icon = TrayIconHost.CreateWindowIcon() };
        flyout.PendingChanged += (_, pending) => _tray.SetPending(pending);
        flyout.SettingsRequested += (_, _) => OpenSettings();
        flyout.AboutRequested += (_, _) => OpenAbout();
        flyout.CalendarRequested += (_, _) => OpenCalendar();
        flyout.MainRequested += (_, _) => OpenMain();
        return flyout;
    }

    /// <summary>
    /// Engancha el atajo global al panel. Necesita un handle: se fuerza sin llegar a mostrar la
    /// ventana. Devuelve si Windows lo ha aceptado.
    /// </summary>
    private bool MontarAtajo()
    {
        var handle = new WindowInteropHelper(_flyout).EnsureHandle();
        _hotkey?.Dispose();
        _hotkey = new GlobalHotkey(handle);
        _hotkey.Pressed += (_, _) => _flyout.ShowFlyout();
        return _hotkey.Register(_settings.Get(SettingsService.KeyHotkey, "Ctrl+Alt+T"));
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
        Ventanas.Mostrar(_main);
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
        Ventanas.Mostrar(_calendar);
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings, _hotkey, _auth, _tasks)
        {
            Icon = TrayIconHost.CreateWindowIcon(),
        };
        Ventanas.Modal(window);
    }

    /// <summary>«Acerca de»: version, contacto, idioma, privacidad y licencia, como en el movil.</summary>
    private void OpenAbout()
    {
        var window = new AboutWindow(_settings)
        {
            Icon = TrayIconHost.CreateWindowIcon(),
        };
        Ventanas.Modal(window);
    }

    private WhatsNewWindow? _whatsNew;

    /// <summary>Novedades de las ultimas versiones (General 6.7), o al frente si ya estaba abierta.</summary>
    public void OpenWhatsNew()
    {
        if (_whatsNew is { IsLoaded: true })
        {
            _whatsNew.Activate();
            return;
        }

        _whatsNew = new WhatsNewWindow(_settings) { Icon = TrayIconHost.CreateWindowIcon() };
        _whatsNew.Closed += (_, _) => _whatsNew = null;
        Ventanas.Mostrar(_whatsNew);
        _whatsNew.Activate();
    }

    // ==================================================================================
    //  Gestor global de excepciones (General 6.12)
    // ==================================================================================

    private static string CrashLog => Path.Combine(Rutas.Carpeta, "crash.log");

    private static bool _gestorInstalado;

    private static DateTime _ultimoAviso = DateTime.MinValue;

    /// <summary>
    /// Un error que no se esperaba nunca cierra la aplicacion: se apunta con su traza en
    /// <c>crash.log</c>, se avisa en el idioma de la aplicacion y se sigue. Es una aplicacion de
    /// bandeja, a menudo sin ninguna ventana a la vista, asi que el aviso es una notificacion
    /// flotante (Growl) y no un dialogo que necesita ventana madre.
    /// </summary>
    private void InstalarGestorDeErrores()
    {
        if (_gestorInstalado)
        {
            return;
        }

        _gestorInstalado = true;
        DispatcherUnhandledException += AlFallarLaInterfaz;
        TaskScheduler.UnobservedTaskException += AlFallarUnaTarea;
        AppDomain.CurrentDomain.UnhandledException += AlFallarElProceso;
    }

    internal static void AlFallarLaInterfaz(object? sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs ex)
    {
        Apuntar("DispatcherUnhandledException", ex.Exception);
        Avisar();
        ex.Handled = true;
    }

    internal static void AlFallarUnaTarea(object? sender, UnobservedTaskExceptionEventArgs ex)
    {
        Apuntar("TaskScheduler.UnobservedTaskException", ex.Exception);
        ex.SetObserved();
    }

    /// <summary>Este no se puede frenar (el proceso ya se va): solo se apunta.</summary>
    internal static void AlFallarElProceso(object? sender, UnhandledExceptionEventArgs ex) =>
        Apuntar("AppDomain.UnhandledException", ex.ExceptionObject as Exception);

    internal static void Apuntar(string origen, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!);

            // Nunca crece sin fin: pasado un cuarto de mega se aparta el viejo y se empieza otro.
            if (File.Exists(CrashLog) && new FileInfo(CrashLog).Length > 256 * 1024)
            {
                File.Move(CrashLog, CrashLog + ".old", overwrite: true);
            }

            var version = typeof(App).Assembly.GetName().Version;
            File.AppendAllText(CrashLog,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] v{version} {origen}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Si ni siquiera se puede escribir el registro, no hay nada mejor que hacer.
        }
    }

    /// <summary>El aviso, sin encadenar: si ya salio uno hace menos de 10 s, solo se apunta.</summary>
    internal static void Avisar()
    {
        if (DateTime.Now - _ultimoAviso < TimeSpan.FromSeconds(10))
        {
            return;
        }

        _ultimoAviso = DateTime.Now;
        try
        {
            Sistema.Actual.Aviso(TipoAviso.Error, $"{Texto("UnexpectedErrorTitle")}{Environment.NewLine}{Texto("UnexpectedError")}");
        }
        catch (Exception ex)
        {
            Apuntar("Aviso de error", ex);
        }
    }

    /// <summary>
    /// El texto en el idioma de la aplicacion. Un error en el arranque puede llegar antes de que
    /// esten cargados los textos (Loc devuelve entonces la clave): se tira del idioma del sistema.
    /// </summary>
    internal static string Texto(string clave)

    {
        var texto = Localization.Loc.Get(clave);
        if (texto != clave)
        {
            return texto;
        }

        var es = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";
        return clave == "UnexpectedErrorTitle"
            ? (es ? "Algo ha fallado" : "Something went wrong")
            : (es ? "Task Manager ha tenido un error inesperado, pero sigue funcionando. Los detalles se han guardado en el registro de errores."
                  : "Task Manager hit an unexpected error but keeps working. The details were saved to the error log.");
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
