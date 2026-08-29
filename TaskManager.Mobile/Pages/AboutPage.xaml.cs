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
        VersionLabel.Text = $"Versión {AppInfo.Current.VersionString}";
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
