namespace TaskManager.Mobile;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Los ajustes se cargan ANTES de construir el Shell, y a la fuerza.
        //
        // El idioma sale de aqui, y los textos de una pagina se resuelven al construirla. Si se
        // dejara para el OnAppearing de la primera pagina, esa pagina ya estaria pintada: se veia
        // la pantalla de entrada en español teniendo la aplicacion en ingles.
        //
        // Es una espera bloqueante en el arranque, que normalmente no se hace. Aqui se acepta
        // porque es una lectura de una tabla diminuta en local y porque la alternativa —pintar en
        // un idioma y corregir despues— se ve.
        try
        {
            Helpers.ServiceHelper.GetRequiredService<Core.Services.SettingsService>()
                   .LoadAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Si los ajustes no se pueden leer, se arranca con los valores por defecto: mejor la
            // aplicacion en el idioma del sistema que ninguna aplicacion.
            System.Diagnostics.Debug.WriteLine($"Ajustes: {ex.Message}");
        }

        var window = new Window(new AppShell());

        // La sincronizacion sigue a la ventana: se pone al dia al abrir y al volver del segundo
        // plano —que es cuando el usuario puede ver el resultado— y se para al irse, donde una
        // ronda cada pocos minutos solo gastaria bateria.
        window.Created += (_, _) =>
        {
            Syncing()?.Start();

            // Y una pasada de fondo cada media hora aunque la aplicacion se cierre: es lo que hace
            // que una tarea escrita en Windows avise aqui sin tener que abrir nada.
#if ANDROID
            Platforms.Android.BackgroundSyncReceiver.Schedule();
#endif
        };
        window.Resumed += (_, _) => Syncing()?.Start();
        window.Stopped += (_, _) => Syncing()?.Stop();

#if DEBUG
        SocShared.AuthorNotes.Attach(window);   // notas de autor: SOLO Debug (anexo E.2)
#endif
        return window;
    }

    /// <summary>
    /// El coordinador, con su aviso de tarea nueva ya enganchado. Se resuelve tarde y una sola vez:
    /// en el constructor de la aplicacion el contenedor aun no esta listo del todo.
    /// </summary>
    private Core.Services.SyncCoordinator? Syncing()
    {
        if (_syncing is not null)
        {
            return _syncing;
        }

        try
        {
            _syncing = Helpers.ServiceHelper.GetRequiredService<Core.Services.SyncCoordinator>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sync: {ex.Message}");
            return null;
        }

        var notifications = Helpers.ServiceHelper.GetRequiredService<Core.Services.INotificationService>();
        var texts = Helpers.ServiceHelper.GetRequiredService<Core.Services.LocalizationService>();

        _syncing.TaskArrived += (_, task) => MainThread.BeginInvokeOnMainThread(() =>
            notifications.Notify(texts["MenuMyTasks"], texts.Format("TaskArrivedFromDevice", task.Title)));

        return _syncing;
    }

    private Core.Services.SyncCoordinator? _syncing;
}
