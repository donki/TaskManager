using System.Windows;
using System.Windows.Navigation;
using TaskManager.Core.Services;

namespace TaskManager.Desktop;

/// <summary>
/// «Acerca de»: la misma pantalla que en Android, con la version, el contacto, el idioma, la
/// privacidad y la licencia.
/// </summary>
/// <remarks>
/// La estructura no se inventa aqui: es la de <c>AboutPage</c> del movil, que a su vez viene de la
/// constitucion de Mobile (seccion 7, pantalla About homogenea en todas las aplicaciones). En
/// Windows faltaba, y con ella faltaba lo unico que dice que version esta corriendo.
/// </remarks>
public partial class AboutWindow : Window
{
    private readonly SettingsService _settings;

    public AboutWindow(SettingsService settings)
    {
        InitializeComponent();

        _settings = settings;

        Services.ThemeManager.StyleTitleBar(this);

        LogoImage.Source = Services.TrayIconHost.CreateWindowIcon();

        // La version que de verdad esta corriendo, leida del propio ejecutable, igual que en Android.
        VersionLabel.Text = $"v{WhatsNewWindow.CurrentVersion()}";
    }

    // -----------------------------------------------------------------------

    private void OnContactClick(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Services.Sistema.Actual.Abrir(e.Uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            Controls.ModernDialog.Alert(this, Localization.Loc.Get("Contact"), ex.Message);
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
    /// ajuste: hay que volver a montarlas. Es lo mismo que hace Ajustes, y por eso esta ventana se
    /// cierra antes — quedaria a medio traducir.
    /// </remarks>
    private async Task UseAsync(string language)
    {
        if (_settings.Get(SettingsService.KeyLanguage) == language)
        {
            return;
        }

        await new LocalizationService(_settings).SetLanguageAsync(language);
        Close();

        if (Application.Current is App app)
        {
            app.RebuildUi();
        }
    }

    private void OnWhatsNewClick(object sender, RoutedEventArgs e)
    {
        Close();
        (Application.Current as App)?.OpenWhatsNew();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
