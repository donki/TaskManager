using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using TaskManager.Core.Services;

namespace TaskManager.Desktop.Views;

/// <summary>
/// «Acerca de»: la misma pantalla que en Android, con la version, el contacto, el idioma, la
/// privacidad y la licencia.
/// </summary>
/// <remarks>
/// <para>La estructura no se inventa aqui: es la de <c>AboutPage</c> del movil, que a su vez viene de
/// la constitucion de Mobile (seccion 7, pantalla About homogenea en todas las aplicaciones).</para>
///
/// <para>Es un control y no una ventana: vive como ultima pestaña de la ventana principal. Antes era
/// una ventana modal aparte, una mas que abrir y cerrar para mirar la version.</para>
/// </remarks>
public partial class AboutView : UserControl
{
    private SettingsService? _settings;

    public AboutView()
    {
        InitializeComponent();

        LogoImage.Source = Services.TrayIconHost.CreateWindowIcon();

        // La version que de verdad esta corriendo, leida del propio ejecutable, igual que en Android.
        VersionLabel.Text = $"v{WhatsNewWindow.CurrentVersion()}";
    }

    /// <summary>
    /// Le da los ajustes, que hacen falta para cambiar el idioma. El XAML no admite constructores
    /// con parametros, asi que entran por aqui, como el servicio del calendario.
    /// </summary>
    public void Attach(SettingsService settings) => _settings = settings;

    // -----------------------------------------------------------------------

    private void OnContactClick(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Services.Sistema.Actual.Abrir(e.Uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            Controls.ModernDialog.Alert(Window.GetWindow(this), Localization.Loc.Get("Contact"), ex.Message);
        }

        e.Handled = true;
    }

    private async void OnSpanishClick(object sender, RoutedEventArgs e) => await UseAsync("es");

    private async void OnEnglishClick(object sender, RoutedEventArgs e) => await UseAsync("en");

    /// <summary>
    /// Cambia el idioma y rehace la interfaz.
    /// </summary>
    /// <remarks>
    /// Los textos del XAML se fijan al construir cada ventana, asi que no basta con guardar el
    /// ajuste: hay que volver a montarlas. Es lo mismo que hace Ajustes; la ventana principal se
    /// rehace tambien y vuelve a abrirse en esta pestaña.
    /// </remarks>
    private async Task UseAsync(string language)
    {
        if (_settings is null || _settings.Get(SettingsService.KeyLanguage) == language)
        {
            return;
        }

        await new LocalizationService(_settings).SetLanguageAsync(language);

        if (Application.Current is App app)
        {
            app.RebuildUi();
        }
    }

    private void OnWhatsNewClick(object sender, RoutedEventArgs e) =>
        (Application.Current as App)?.OpenWhatsNew();
}
