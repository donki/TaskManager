namespace TaskManager.Mobile.Pages;

/// <summary>
/// Pantalla Acerca de, homogenea con el resto de apps sOCratic (constitucion Mobile 7): logo,
/// version, contacto, privacidad y licencia.
/// </summary>
public partial class AboutPage : ContentPage
{
    private const string ContactEmail = "jsoladelarosa@gmail.com";

    public AboutPage()
    {
        InitializeComponent();
        VersionLabel.Text = $"v{AppInfo.Current.VersionString}";
        ShowLanguage();
    }

    /// <summary>El idioma activo se resalta, para que se vea cual esta puesto sin adivinarlo.</summary>
    private void ShowLanguage()
    {
        var spanish = Localization.Loc.Instance.Language == "es";

        SpanishButton.Opacity = spanish ? 1 : 0.5;
        EnglishButton.Opacity = spanish ? 0.5 : 1;
    }

    private async void OnSpanishClicked(object? sender, EventArgs e) => await SetLanguageAsync("es");

    private async void OnEnglishClicked(object? sender, EventArgs e) => await SetLanguageAsync("en");

    /// <summary>
    /// El cambio se aplica al momento: los textos del XAML estan enlazados al servicio de idiomas,
    /// asi que basta con avisar de que cambio y toda la interfaz se repinta sin reiniciar.
    /// </summary>
    private async Task SetLanguageAsync(string language)
    {
        await Localization.Loc.Instance.SetLanguageAsync(language);
        ShowLanguage();

        // Se reconstruye el Shell entero. Avisar del cambio del indexador NO basta: los enlaces de
        // MAUI no reevaluan un indexador de cadena con "Item[]" (comprobado en tablet el
        // 2026-08-30: el ajuste se guardaba y la pantalla seguia en el idioma anterior). Rehacer las
        // paginas es menos fino, pero es lo unico que garantiza que TODO quede traducido al momento.
        // El precio es volver a Mi Dia, que en un cambio de idioma es asumible.
        if (Application.Current?.Windows.FirstOrDefault() is { } window)
        {
            window.Page = new AppShell();
        }
    }

    private async void OnContactClicked(object? sender, EventArgs e)
    {
        try
        {
            await Email.Default.ComposeAsync(new EmailMessage
            {
                Subject = "Task Manager",
                To = [ContactEmail],
            });
        }
        catch (Exception)
        {
            // Sin cliente de correo configurado se copia la direccion: el usuario puede escribir
            // desde donde quiera, en vez de quedarse sin manera de contactar.
            await Clipboard.Default.SetTextAsync(ContactEmail);
            await SocShared.ModernDialog.AlertAsync(this, "Contacto",
                $"Dirección copiada: {ContactEmail}", "OK");
        }
    }
}
