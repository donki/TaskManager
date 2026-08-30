using TaskManager.Core;
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

        DisplayNameBox.Text = settings.DisplayName;
        HotkeyBox.Text = settings.Get(SettingsService.KeyHotkey, "Ctrl+Alt+T");

        FillLanguages();
        AutoStartBox.IsChecked = AutoStart.IsEnabled;
        SoundBox.IsChecked = settings.SoundEnabled;

        ShowAccount();
    }

    // -----------------------------------------------------------------------
    // Cuenta
    // -----------------------------------------------------------------------

    private void ShowAccount()
    {
        if (!AuthOptions.GoogleSignInEnabled)
        {
            var id = _settings.InstallationId;
            AccountLabel.Text = id.Length >= 8
                ? Localization.Loc.Format("ThisComputerId", id[..8])
                : Localization.Loc.Get("ThisComputer");
            SignInButton.IsEnabled = false;
            SignOutButton.IsEnabled = false;
            return;
        }

        var user = _auth.CurrentUser;

        AccountLabel.Text = user is not null
            ? $"{user.DisplayName} · {user.Email}"
            : Localization.Loc.Get("NoAccountDesktop");

        SignInButton.IsEnabled = user is null && _settings.IsSupabaseConfigured;
        SignOutButton.IsEnabled = user is not null;
    }

    private async void OnSignInClick(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        StatusLabel.Text = Localization.Loc.Get("SigningInBrowser");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var user = await _auth.SignInWithGoogleAsync(cts.Token);

            // Lo hecho sin cuenta pasa a la cuenta: el nivel y las rachas no se pierden.
            await _tasks.AdoptAccountAsync(user.Id);
            StatusLabel.Text = Localization.Loc.Format("SignedInAs", user.Email);
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
            ShowAccount();
        }
    }

    private async void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        await _auth.SignOutAsync();
        StatusLabel.Text = Localization.Loc.Get("SessionClosed");
        ShowAccount();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        await _settings.SetAsync(SettingsService.KeyDisplayName, DisplayNameBox.Text.Trim());
        await _settings.SetBoolAsync(SettingsService.KeySound, SoundBox.IsChecked == true);

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
