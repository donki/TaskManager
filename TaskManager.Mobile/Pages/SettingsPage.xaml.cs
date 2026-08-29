using TaskManager.Core;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// Ajustes: celebracion, servidor de IA local, sincronizacion e identidad. Se guardan en la misma
/// tabla que usa la aplicacion de escritorio (SettingsService), con las mismas claves.
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly SettingsService _settings;
    private readonly SupabaseAuthService _auth;
    private readonly TaskService _tasks;
    private readonly INotificationService _notifications;

    private bool _loading;

    public SettingsPage()
        : this(ServiceHelper.GetRequiredService<SettingsService>(),
               ServiceHelper.GetRequiredService<SupabaseAuthService>(),
               ServiceHelper.GetRequiredService<TaskService>(),
               ServiceHelper.GetRequiredService<INotificationService>())
    {
    }

    public SettingsPage(
        SettingsService settings,
        SupabaseAuthService auth,
        TaskService tasks,
        INotificationService notifications)
    {
        InitializeComponent();
        _settings = settings;
        _auth = auth;
        _tasks = tasks;
        _notifications = notifications;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _settings.LoadAsync();

        // Sin esta bandera, rellenar los interruptores dispara Toggled y reescribe los ajustes.
        _loading = true;
        HapticsSwitch.IsToggled = _settings.HapticsEnabled;
        SoundSwitch.IsToggled = _settings.SoundEnabled;
        NotifySwitch.IsToggled = _settings.NotificationsEnabled;
        NotifyTimePicker.Time = TimeSpan.FromHours(_settings.NotifyHour);
        NotifyHourRow.IsVisible = _settings.NotificationsEnabled;
        _loading = false;

        DisplayNameEntry.Text = _settings.DisplayName;

        if (AuthOptions.GoogleSignInEnabled)
            await _auth.RestoreSessionAsync();

        ShowAccount();
    }

    // -----------------------------------------------------------------------
    // Cuenta
    // -----------------------------------------------------------------------

    private void ShowAccount()
    {
        // Sin entrada con Google, la identidad es esta instalacion: no hay nada que pulsar, solo
        // que ensenar. Se muestran los primeros caracteres, que es lo util para reconocer el
        // dispositivo sin llenar la pantalla con un GUID entero.
        if (!AuthOptions.GoogleSignInEnabled)
        {
            var id = _settings.InstallationId;
            AccountNameLabel.Text = "Este dispositivo";
            AccountEmailLabel.Text = id.Length >= 8 ? $"Instalación {id[..8]}" : id;
            SignInButton.IsVisible = false;
            SignOutButton.IsVisible = false;
            AccountHintLabel.Text = "Tus tareas van ligadas a esta instalación. Si desinstalas la aplicación, se pierden.";
            return;
        }

        var user = _auth.CurrentUser;

        AccountNameLabel.Text = user?.DisplayName ?? "Sin cuenta";
        AccountEmailLabel.Text = user?.Email ?? "Las tareas se quedan en este dispositivo";
        SignInButton.IsVisible = user is null;
        SignOutButton.IsVisible = user is not null;
        SignInButton.IsEnabled = _settings.IsSupabaseConfigured;

        AccountHintLabel.Text = user is not null
            ? "Tus listas y tu progreso van con la cuenta."
            : "Entra con Google para guardar tu usuario y compartir listas con tus grupos.";
    }

    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        SignInButton.IsEnabled = false;
        try
        {
            var user = await _auth.SignInWithGoogleAsync();

            // Lo hecho sin cuenta pasa a la cuenta: el nivel y las rachas no se pierden.
            await _tasks.AdoptAccountAsync(user.Id);
            await _settings.SetBoolAsync(SettingsService.KeyAuthSkipped, false);
            ShowAccount();
        }
        catch (TaskCanceledException)
        {
            ShowAccount();
        }
        catch (Exception ex)
        {
            await SocShared.ModernDialog.AlertAsync(this, "No se pudo entrar", ex.Message, "OK");
            ShowAccount();
        }
        finally
        {
            SignInButton.IsEnabled = _settings.IsSupabaseConfigured;
        }
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        var confirmed = await SocShared.ModernDialog.AlertAsync(this, "Cerrar sesión",
            "Las tareas se quedan en este dispositivo. Podrás volver a entrar cuando quieras.",
            "Cerrar sesión", "Cancelar");

        if (confirmed)
        {
            await _auth.SignOutAsync();

            // Al salir se vuelve a preguntar en el proximo arranque.
            await _settings.SetBoolAsync(SettingsService.KeyAuthSkipped, false);
            ShowAccount();
        }
    }

    /// <summary>
    /// Al encender los recordatorios se pide el permiso en ese momento, no al abrir la aplicacion:
    /// pedirlo antes de que haga falta es la forma mas rapida de que lo denieguen.
    /// </summary>
    private async void OnNotifyToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        NotifyHourRow.IsVisible = e.Value;

        if (!e.Value)
        {
            await _settings.SetBoolAsync(SettingsService.KeyNotifyEnabled, false);
            _notifications.CancelDailySummary();
            return;
        }

        if (!await _notifications.RequestPermissionAsync())
        {
            // Sin permiso no hay aviso posible: el interruptor vuelve a su sitio en vez de mentir.
            _loading = true;
            NotifySwitch.IsToggled = false;
            NotifyHourRow.IsVisible = false;
            _loading = false;

            await SocShared.ModernDialog.AlertAsync(this, "Sin permiso",
                "Android no permite mostrar avisos hasta que se conceda el permiso de notificaciones.",
                "OK");
            return;
        }

        await _settings.SetBoolAsync(SettingsService.KeyNotifyEnabled, true);
        _notifications.ScheduleDailySummary(NotifyTimePicker.Time ?? TimeSpan.FromHours(9));
    }

    private async void OnNotifyTimeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_loading || e.PropertyName != nameof(TimePicker.Time) || !NotifySwitch.IsToggled)
        {
            return;
        }

        await _settings.SetAsync(SettingsService.KeyNotifyHour, ((int)(NotifyTimePicker.Time ?? TimeSpan.FromHours(9)).TotalHours).ToString());
        _notifications.ScheduleDailySummary(NotifyTimePicker.Time ?? TimeSpan.FromHours(9));
    }

    private async void OnHapticsToggled(object? sender, ToggledEventArgs e)
    {
        if (!_loading)
        {
            await _settings.SetBoolAsync(SettingsService.KeyHaptics, e.Value);
        }
    }

    private async void OnSoundToggled(object? sender, ToggledEventArgs e)
    {
        if (!_loading)
        {
            await _settings.SetBoolAsync(SettingsService.KeySound, e.Value);
        }
    }

    private async void OnSaveName(object? sender, EventArgs e) =>
        await _settings.SetAsync(SettingsService.KeyDisplayName, DisplayNameEntry.Text?.Trim() ?? string.Empty);
}
