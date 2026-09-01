using System.Linq;
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
        FillSnooze();
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

    /// <summary>Cada cuanto insiste el aviso de pendientes, en minutos.</summary>
    private static readonly int[] SnoozeChoices = [0, 15, 30, 60, 120, 240];

    /// <summary>
    /// Rellena las opciones de repeticion del aviso.
    /// </summary>
    /// <remarks>
    /// «No repetir» es la primera y la de siempre: un aviso al dia. Las demas estan para quien
    /// necesita que le insistan, que es justo el caso en el que un unico aviso por la mañana no
    /// sirve de nada.
    /// </remarks>
    private void FillSnooze()
    {
        SnoozePicker.ItemsSource = SnoozeChoices.Select(Describe).ToList();
        SnoozePicker.SelectedIndex = Math.Max(0, Array.IndexOf(SnoozeChoices, _settings.SnoozeMinutes));
        SnoozeRow.IsVisible = _settings.NotificationsEnabled;
    }

    private static string Describe(int minutes) => minutes switch
    {
        0 => Localization.Loc.Instance["SnoozeOff"],
        60 => Localization.Loc.Instance["SnoozeHour"],
        < 60 => Localization.Loc.Instance.Format("SnoozeMinutes", minutes),
        _ => Localization.Loc.Instance.Format("SnoozeHours", minutes / 60),
    };

    private async void OnSnoozeChanged(object? sender, EventArgs e)
    {
        var minutes = SnoozeChoices[Math.Clamp(SnoozePicker.SelectedIndex, 0, SnoozeChoices.Length - 1)];
        await _settings.SetAsync(SettingsService.KeySnoozeMinutes, minutes.ToString());

        // Reprogramar al momento: el ajuste no puede quedarse esperando al proximo arranque.
        _notifications.ScheduleDailySummary(TimeSpan.FromHours(_settings.NotifyHour));
    }

    private void ShowAccount()
    {
        var user = _auth.CurrentUser;

        // La foto de la cuenta al lado del nombre. Solo se enseña si hay: si no, se queda el
        // icono generico que hay debajo.
        var avatar = _settings.AvatarUrl;
        AvatarFrame.IsVisible = user is not null && avatar.Length > 0;
        AvatarImage.Source = AvatarFrame.IsVisible ? ImageSource.FromUri(new Uri(avatar)) : null;

        AccountNameLabel.Text = user?.DisplayName ?? "Sin cuenta";
        AccountEmailLabel.Text = user?.Email ?? "Hay que entrar con Google para usar la aplicación";
        SignInButton.IsVisible = user is null;
        SignOutButton.IsVisible = user is not null;
        SignInButton.IsEnabled = _auth.IsConfigured;

        AccountHintLabel.Text = user is not null
            ? "Tus listas y tu progreso van con la cuenta de Google."
            : "La entrada es obligatoria: sin cuenta no se puede usar la aplicación.";
    }

    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        SignInButton.IsEnabled = false;
        try
        {
            var user = await _auth.SignInAsync(IdentityProvider.Google);

            // Lo hecho sin cuenta pasa a la cuenta: el nivel y las rachas no se pierden.
            await _tasks.AdoptAccountAsync(user.Id);
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
            SignInButton.IsEnabled = _auth.IsConfigured;
        }
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        var confirmed = await SocShared.ModernDialog.AlertAsync(this, "Cerrar sesión",
            "Sin cuenta no se puede usar la aplicación: volverá a la pantalla de entrada.",
            "Cerrar sesión", "Cancelar");

        if (confirmed)
        {
            await _auth.SignOutAsync();
            ShowAccount();

            // Sin usuario no hay nada que enseñar: se vuelve a la puerta.
            await Shell.Current.GoToAsync("//LoginPage");
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
            SnoozeRow.IsVisible = false;
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
