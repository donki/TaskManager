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
#if DEBUG
        SocShared.AuthorNotes.Attach(window);   // notas de autor: SOLO Debug (anexo E.2)
#endif
        return window;
    }
}
