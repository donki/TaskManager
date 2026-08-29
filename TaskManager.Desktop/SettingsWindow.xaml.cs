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
                ? $"Este equipo · instalación {id[..8]}"
                : "Este equipo";
            SignInButton.IsEnabled = false;
            SignOutButton.IsEnabled = false;
            return;
        }

        var user = _auth.CurrentUser;

        AccountLabel.Text = user is not null
            ? $"{user.DisplayName} · {user.Email}"
            : "Sin cuenta: entra con Google para guardar tu usuario y compartir listas.";

        SignInButton.IsEnabled = user is null && _settings.IsSupabaseConfigured;
        SignOutButton.IsEnabled = user is not null;
    }

    private async void OnSignInClick(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        StatusLabel.Text = "Terminando la entrada en el navegador...";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var user = await _auth.SignInWithGoogleAsync(cts.Token);

            // Lo hecho sin cuenta pasa a la cuenta: el nivel y las rachas no se pierden.
            await _tasks.AdoptAccountAsync(user.Id);
            StatusLabel.Text = $"Dentro como {user.Email}.";
        }
        catch (TaskCanceledException)
        {
            StatusLabel.Text = "Entrada cancelada.";
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
        StatusLabel.Text = "Sesión cerrada.";
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
            StatusLabel.Text = $"El atajo {combination} no está disponible; se mantiene el anterior.";
            _hotkey.Register(_settings.Get(SettingsService.KeyHotkey, "Ctrl+Alt+T"));
            return;
        }

        await _settings.SetAsync(SettingsService.KeyHotkey, combination);
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
