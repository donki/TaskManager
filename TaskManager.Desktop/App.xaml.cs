using System.IO;
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
    private CalendarWindow? _calendar;
    private ReminderScheduler? _reminders;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ThemeManager.Apply();

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Socratic", "TaskManager");
        Directory.CreateDirectory(folder);

        _database = new LocalDatabase(Path.Combine(folder, "taskmanager.db3"));
        var repository = new TaskRepository(_database);
        _settings = new SettingsService(_database);
        await _settings.LoadAsync();

        // El idioma se resuelve antes de crear ninguna ventana: los textos del XAML se fijan al
        // construirla, asi que leerlo despues dejaria la primera ventana en el idioma equivocado.
        Localization.Loc.Use(new LocalizationService(_settings));

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

        // Entrada con Google: navegador del sistema + servidor local de un solo uso, y los tokens
        // cifrados con DPAPI. La sesion se recupera sola mientras el refresco siga siendo valido.
        _auth = new SupabaseAuthService(_http, _settings, new DpapiTokenStore(folder), new LoopbackOAuthBrowser());
        await _auth.RestoreSessionAsync();

        // Sincronizacion con el movil. Se lanza sin esperarla: si la red va lenta o no hay, el
        // panel tiene que abrirse igual de rapido; las tareas locales ya estan.
        _sync = SupabaseConfig.IsConfigured
            ? new SupabaseSyncService(_http, repository, _settings, _auth)
            : new LocalOnlySyncService(repository);

        _ = SyncInBackgroundAsync();

        _flyout = new FlyoutWindow(_tasks, _settings) { Icon = TrayIconHost.CreateWindowIcon() };
        _flyout.PendingChanged += (_, pending) => _tray.SetPending(pending);
        _flyout.SettingsRequested += (_, _) => OpenSettings();
        _flyout.CalendarRequested += (_, _) => OpenCalendar();

        _tray = new TrayIconHost();
        _tray.Activated += (_, _) => _flyout.ShowFlyout();
        _tray.SettingsRequested += (_, _) => OpenSettings();
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

        _tray.SetPending(await repository.CountMyDayPendingAsync());

        // Recordatorios: el aviso diario y el de las tareas que vencen hoy, como globo de bandeja.
        _reminders = new ReminderScheduler(repository, _settings, _tray);

        // Siempre arranca en la bandeja, se abra como se abra: es una aplicacion de bandeja, y
        // plantar el panel en pantalla al encender el equipo estorba mas que ayuda. Se despliega
        // con el clic en el icono o con el atajo global.
        _tray.Notify("Task Manager", Localization.Loc.Format("TrayRunning",
            _settings.Get(SettingsService.KeyHotkey, "Ctrl+Alt+T")));
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
        var pending = _tray.Pending;

        _flyout.CloseForReal();
        _flyout = new FlyoutWindow(_tasks, _settings) { Icon = TrayIconHost.CreateWindowIcon() };
        _flyout.PendingChanged += (_, count) => _tray.SetPending(count);
        _flyout.SettingsRequested += (_, _) => OpenSettings();
        _flyout.CalendarRequested += (_, _) => OpenCalendar();

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
    /// Sube lo pendiente y baja lo que haya hecho el movil, y refresca el panel si algo cambio.
    /// </summary>
    /// <remarks>
    /// Se traga los fallos a proposito: quedarse sin conexion no es un error que haya que
    /// ensenarle a nadie, y la cola de salida no se pierde. Cuando vuelva la red, subira.
    /// </remarks>
    private async Task SyncInBackgroundAsync()
    {
        try
        {
            await _sync.StartAsync();
            await _flyout.ReloadAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sync: {ex.Message}");
        }
    }

    /// <summary>
    /// Abre el calendario, o trae al frente el que ya estuviera abierto.
    /// </summary>
    /// <remarks>
    /// Se guarda la referencia para no acabar con cinco calendarios apilados cuando se pulsa el
    /// boton varias veces, que es lo que pasa si cada clic crea una ventana nueva.
    /// </remarks>
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

    protected override void OnExit(ExitEventArgs e)
    {
        _reminders?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _http?.Dispose();
        base.OnExit(e);
    }
}
