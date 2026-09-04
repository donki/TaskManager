using TaskManager.Core;
using System.Linq;
using System.Windows;
using TaskManager.Core.Services;
using TaskManager.Desktop.Services;

namespace TaskManager.Desktop;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly GlobalHotkey? _hotkey;
    private readonly SupabaseAuthService _auth;
    private readonly TaskService _tasks;

    public SettingsWindow(SettingsService settings, GlobalHotkey? hotkey, SupabaseAuthService auth, TaskService tasks)
    {
        InitializeComponent();

        _settings = settings;
        _hotkey = hotkey;
        _auth = auth;
        _tasks = tasks;

        HotkeyBox.Text = settings.Get(SettingsService.KeyHotkey, "Ctrl+Alt+T");

        Services.ThemeManager.StyleTitleBar(this);

        FillLanguages();
        NotifyBox.IsChecked = settings.NotificationsEnabled;
        FillSnooze();

        AutoStartBox.IsChecked = AutoStart.IsEnabled;
        SoundBox.IsChecked = settings.SoundEnabled;

        ShowAccount();
    }

    // -----------------------------------------------------------------------
    // Cuenta
    // -----------------------------------------------------------------------

    /// <summary>
    /// La cuenta no se elige aqui: la entrada es obligatoria y ya se hizo al arrancar. Lo unico
    /// que queda es ver quien esta dentro y poder salir, que abre otra vez la puerta.
    /// </summary>
    /// <summary>Cada cuanto insiste el aviso de pendientes. Se guarda en minutos.</summary>
    private static readonly int[] SnoozeChoices = [0, 15, 30, 60, 120, 240];

    /// <summary>
    /// Rellena las opciones de repeticion del aviso.
    /// </summary>
    /// <remarks>
    /// «No repetir» es la primera y la de siempre: un aviso al dia. Las demas estan para quien
    /// necesita que le insistan, que es justo el caso en el que un unico aviso a las nueve de la
    /// mañana no sirve de nada.
    /// </remarks>
    private void FillSnooze()
    {
        SnoozeBox.ItemsSource = SnoozeChoices.Select(Describe).ToList();
        SnoozeBox.SelectedIndex = Math.Max(0, Array.IndexOf(SnoozeChoices, _settings.SnoozeMinutes));
        SnoozeBox.IsEnabled = NotifyBox.IsChecked == true;
    }

    private static string Describe(int minutes) => minutes switch
    {
        0 => Localization.Loc.Get("SnoozeOff"),
        60 => Localization.Loc.Get("SnoozeHour"),
        < 60 => Localization.Loc.Format("SnoozeMinutes", minutes),
        _ => Localization.Loc.Format("SnoozeHours", minutes / 60),
    };

    private void OnNotifyToggled(object sender, RoutedEventArgs e) =>
        SnoozeBox.IsEnabled = NotifyBox.IsChecked == true;

    private void ShowAccount()
    {
        var user = _auth.CurrentUser;
        var provider = CurrentProvider();

        AccountLabel.Text = user is not null
            ? $"{user.DisplayName} · {user.Email} ({provider})"
            : Localization.Loc.Get("NoAccountDesktop");

        ShowAvatar(user is null ? string.Empty : _settings.AvatarUrl);

        // El nombre de la aplicacion es el de la cuenta: se enseña, no se edita.
        DisplayNameBox.Text = user?.DisplayName ?? _settings.DisplayName;

        // El boton de la cuenta con la que ya se esta dentro se apaga: pulsarlo abriria el
        // navegador para acabar donde ya se estaba.
        GoogleButton.IsEnabled = _auth.IsConfiguredFor(IdentityProvider.Google)
            && provider != IdentityProvider.Google;
        MicrosoftButton.Visibility = TaskManager.Core.AuthOptions.MicrosoftSignInEnabled
            && _auth.IsConfiguredFor(IdentityProvider.Microsoft)
            ? Visibility.Visible
            : Visibility.Collapsed;
        MicrosoftButton.IsEnabled = provider != IdentityProvider.Microsoft;

        SignOutButton.IsEnabled = user is not null;
    }

    /// <summary>Con que proveedor se entro, o null si no hay nadie dentro.</summary>
    private IdentityProvider? CurrentProvider()
    {
        if (_auth.CurrentUser is null)
        {
            return null;
        }

        return Enum.TryParse<IdentityProvider>(
            _settings.Get(SettingsService.KeyAuthProvider, nameof(IdentityProvider.Google)), out var parsed)
            ? parsed
            : IdentityProvider.Google;
    }

    /// <summary>
    /// Pinta la foto de la cuenta dentro de un circulo.
    /// </summary>
    /// <remarks>
    /// Se descarga sin bloquear y <b>se traga los fallos</b>: una foto que no carga —sin red, o el
    /// enlace caducado— no puede impedir ver los ajustes ni tumbar la ventana. Si falla, el hueco
    /// sencillamente no se enseña.
    /// </remarks>
    private void ShowAvatar(string url)
    {
        if (url.Length == 0)
        {
            AvatarCircle.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(url, UriKind.Absolute);
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.EndInit();

            AvatarCircle.Fill = new System.Windows.Media.ImageBrush(image)
            {
                Stretch = System.Windows.Media.Stretch.UniformToFill,
            };

            AvatarCircle.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            AvatarCircle.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnGoogleClick(object sender, RoutedEventArgs e) =>
        await SignInAsync(IdentityProvider.Google);

    private async void OnMicrosoftClick(object sender, RoutedEventArgs e) =>
        await SignInAsync(IdentityProvider.Microsoft);

    /// <summary>
    /// Entra con el proveedor elegido, que es tambien como se <b>cambia de cuenta</b>.
    /// </summary>
    /// <remarks>
    /// <para>Cada cuenta tiene sus listas en este mismo aparato, asi que cambiar no borra ni mueve
    /// nada: lo de la anterior se queda donde estaba y vuelve a aparecer entero al entrar otra vez
    /// con ella.</para>
    ///
    /// <para>Al volver hay que rehacer la interfaz (<see cref="App.ReloadForAccount"/>): el panel,
    /// el contador de la bandeja y las ventanas abiertas estan enseñando las listas de la cuenta
    /// que acaba de salir.</para>
    /// </remarks>
    private async Task SignInAsync(IdentityProvider provider)
    {
        GoogleButton.IsEnabled = false;
        MicrosoftButton.IsEnabled = false;
        StatusLabel.Text = Localization.Loc.Get("SigningInBrowser");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var user = await _auth.SignInAsync(provider, cts.Token);

            // Lo que no era de ninguna cuenta pasa a esta, y si no tiene ninguna lista se le crea
            // la primera: una cuenta recien estrenada no puede aparecer sin nada donde escribir.
            await _tasks.AdoptAccountAsync(user.Id);
            StatusLabel.Text = Localization.Loc.Format("SignedInAs", user.Email);

            ShowAccount();

            if (Application.Current is App app)
            {
                app.ReloadForAccount();
            }

            return;
        }
        catch (OperationCanceledException)
        {
            // Vale para las dos formas de cortar: el tope de tres minutos y la vuelta a la ventana
            // sin haber terminado en el navegador.
            StatusLabel.Text = Localization.Loc.Get("SignInCancelled");
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }

        ShowAccount();
    }

    /// <summary>
    /// Salir deja la aplicacion sin usuario, y sin usuario no hay aplicacion: se vuelve a pedir la
    /// entrada al momento, y si nadie entra, la propia puerta apaga el programa.
    /// </summary>
    private async void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        await _auth.SignOutAsync();
        StatusLabel.Text = Localization.Loc.Get("SessionClosed");
        ShowAccount();

        var login = new LoginWindow(_auth) { Icon = Services.TrayIconHost.CreateWindowIcon() };
        login.ShowDialog();

        if (login.User is not null)
        {
            await _tasks.AdoptAccountAsync(login.User.Id);

            // Puede haber entrado con la otra cuenta: lo que hay en pantalla es de la anterior.
            if (Application.Current is App app)
            {
                app.ReloadForAccount();
            }
        }

        ShowAccount();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // El nombre no se guarda: viene de la cuenta con la que se entro y se refresca al entrar.
        await _settings.SetBoolAsync(SettingsService.KeySound, SoundBox.IsChecked == true);
        await _settings.SetBoolAsync(SettingsService.KeyNotifyEnabled, NotifyBox.IsChecked == true);
        await _settings.SetAsync(SettingsService.KeySnoozeMinutes,
            SnoozeChoices[Math.Clamp(SnoozeBox.SelectedIndex, 0, SnoozeChoices.Length - 1)].ToString());

        AutoStart.Set(AutoStartBox.IsChecked == true);

        // El atajo se guarda solo si el sistema lo acepta: si no, quedaria un ajuste que miente.
        var combination = HotkeyBox.Text.Trim();
        if (_hotkey is not null && !_hotkey.Register(combination))
        {
            StatusLabel.Text = Localization.Loc.Format("HotkeyUnavailable", combination);
            _hotkey.Register(_settings.Get(SettingsService.KeyHotkey, "Ctrl+Alt+T"));
            return;
        }

        await _settings.SetAsync(SettingsService.KeyHotkey, combination);

        // El idioma se guarda el ultimo: si cambia, la aplicacion recrea sus ventanas, y hacerlo
        // antes de guardar el resto se llevaria por delante lo que aun no se hubiera escrito.
        var language = LanguageBox.SelectedIndex switch { 1 => "es", 2 => "en", _ => string.Empty };
        var changed = language != _settings.Get(SettingsService.KeyLanguage);

        if (changed)
        {
            await new LocalizationService(_settings).SetLanguageAsync(language);
        }

        DialogResult = true;
        Close();

        if (changed && Application.Current is App app)
        {
            app.RebuildUi();
        }
    }

    /// <summary>
    /// Opciones de idioma. La primera sigue al sistema, que es lo que hace la aplicacion mientras
    /// nadie elija nada.
    /// </summary>
    private void FillLanguages()
    {
        LanguageBox.Items.Add(Localization.Loc.Get("LanguageSystem"));
        LanguageBox.Items.Add("Español");
        LanguageBox.Items.Add("English");

        LanguageBox.SelectedIndex = _settings.Get(SettingsService.KeyLanguage) switch
        {
            "es" => 1,
            "en" => 2,
            _ => 0,
        };
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
