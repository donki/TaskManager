using System.ComponentModel;
using System.Windows;
using TaskManager.Core.Services;

namespace TaskManager.Desktop;

/// <summary>
/// La puerta de la aplicacion en Windows.
/// </summary>
/// <remarks>
/// <para>La entrada es obligatoria (<see cref="TaskManager.Core.AuthOptions"/>), asi que esta
/// ventana no tiene "seguir sin cuenta" ni se puede esquivar: cerrarla sin haber entrado apaga la
/// aplicacion, porque una aplicacion de bandeja que se quedara viva sin usuario seria un icono que
/// no sabe de quien son las tareas que enseña.</para>
///
/// <para>Es modal y se muestra <b>antes</b> de montar la bandeja: el resto del arranque necesita
/// saber quien es el usuario para atribuirle lo que haga.</para>
/// </remarks>
public partial class LoginWindow : Window
{
    private readonly SupabaseAuthService _auth;
    private bool _signedIn;

    public LoginWindow(SupabaseAuthService auth)
    {
        InitializeComponent();
        _auth = auth;

        StatusLabel.Text = auth.IsConfigured
            ? string.Empty
            : Localization.Loc.Get("OAuthNoClientId");

        // Un boton por cuenta ofrecible. Si falta el identificador de cliente de una, su boton no
        // se enseña: un boton que siempre falla es peor que no tenerlo.
        GoogleButton.Visibility = Visible(auth.IsConfiguredFor(IdentityProvider.Google));

        // Microsoft esta oculto de momento (AuthOptions.MicrosoftSignInEnabled): el flujo esta
        // escrito y probado, pero no se ofrece hasta comprobarlo de verdad en un dispositivo.
        MicrosoftButton.Visibility = Visible(
            TaskManager.Core.AuthOptions.MicrosoftSignInEnabled &&
            auth.IsConfiguredFor(IdentityProvider.Microsoft));
    }

    /// <summary>Quien ha entrado. Null si la ventana se cerro sin entrar.</summary>
    public AuthUser? User { get; private set; }

    private static Visibility Visible(bool yes) => yes ? Visibility.Visible : Visibility.Collapsed;

    private async void OnGoogleClick(object sender, RoutedEventArgs e) =>
        await SignInAsync(IdentityProvider.Google);

    private async void OnMicrosoftClick(object sender, RoutedEventArgs e) =>
        await SignInAsync(IdentityProvider.Microsoft);

    private async Task SignInAsync(IdentityProvider provider)
    {
        SetBusy(true);
        try
        {
            // Tres minutos: lo que puede tardar alguien en entrar en el navegador con dos pasos.
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            User = await _auth.SignInAsync(provider, cts.Token);
            _signedIn = true;

            // Asignar DialogResult ya cierra la ventana modal: no hace falta Close().
            DialogResult = true;
        }
        catch (TaskCanceledException)
        {
            StatusLabel.Text = Localization.Loc.Get("SignInCancelled");
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnQuitClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>Cerrar sin haber entrado es salir: no hay aplicacion sin usuario.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (!_signedIn)
        {
            Application.Current.Shutdown();
        }
    }

    private void SetBusy(bool busy)
    {
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        GoogleButton.IsEnabled = !busy;
        MicrosoftButton.IsEnabled = !busy;
        QuitButton.IsEnabled = !busy;

        if (busy)
        {
            StatusLabel.Text = Localization.Loc.Get("SigningInBrowser");
        }
    }
}
