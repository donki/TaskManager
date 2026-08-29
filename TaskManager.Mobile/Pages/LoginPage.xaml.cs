using TaskManager.Core;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// Entrada con Google (especificacion 2.C). La cuenta es lo que permite guardar el usuario y
/// compartir listas con un grupo; sin ella la aplicacion sigue funcionando, pero solo en este
/// dispositivo.
/// </summary>
public partial class LoginPage : ContentPage
{
    private readonly SupabaseAuthService _auth;
    private readonly TaskService _tasks;
    private readonly SettingsService _settings;

    public LoginPage()
        : this(ServiceHelper.GetRequiredService<SupabaseAuthService>(),
               ServiceHelper.GetRequiredService<TaskService>(),
               ServiceHelper.GetRequiredService<SettingsService>())
    {
    }

    public LoginPage(SupabaseAuthService auth, TaskService tasks, SettingsService settings)
    {
        InitializeComponent();
        _auth = auth;
        _tasks = tasks;
        _settings = settings;
    }

    /// <summary>
    /// Esta pagina es la primera ruta del Shell, asi que hace de puerta. Se aparta sola cuando no
    /// tiene nada que pedir: sin backend configurado, con una sesion ya guardada, o si el usuario
    /// ya dijo que sigue sin cuenta.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _settings.LoadAsync();

        // Sin entrada con Google (o sin servidor) esta pantalla no tiene nada que pedir: la
        // identidad es el identificador de instalacion y se entra directo a Mi Dia.
        if (!AuthOptions.GoogleSignInEnabled
            || !_settings.IsSupabaseConfigured
            || _settings.GetBool(SettingsService.KeyAuthSkipped, false))
        {
            await Shell.Current.GoToAsync("//MyDayPage");
            return;
        }

        SetBusy(true);
        var restored = await _auth.RestoreSessionAsync();
        SetBusy(false);

        if (restored is not null)
        {
            await Shell.Current.GoToAsync("//MyDayPage");
        }
    }

    private async void OnGoogleClicked(object? sender, EventArgs e)
    {
        SetBusy(true);
        try
        {
            var user = await _auth.SignInWithGoogleAsync();

            // Lo hecho antes de entrar pasa a la cuenta: el nivel y las rachas no se pierden.
            await _tasks.AdoptAccountAsync(user.Id);

            await Shell.Current.GoToAsync("//MyDayPage");
        }
        catch (TaskCanceledException)
        {
            ShowStatus("Entrada cancelada.");
        }
        catch (AuthException ex)
        {
            ShowStatus(ex.Message);
        }
        catch (Exception ex)
        {
            ShowStatus($"No se pudo entrar: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnSkipClicked(object? sender, EventArgs e)
    {
        // Se recuerda la decision: preguntar en cada arranque seria un peaje, no una ayuda.
        await _settings.SetBoolAsync(SettingsService.KeyAuthSkipped, true);
        await Shell.Current.GoToAsync("//MyDayPage");
    }

    private void SetBusy(bool busy)
    {
        Busy.IsRunning = busy;
        Busy.IsVisible = busy;
        GoogleButton.IsEnabled = !busy && _settings.IsSupabaseConfigured;
        SkipButton.IsEnabled = !busy;
    }

    private void ShowStatus(string text)
    {
        StatusLabel.Text = text;
        StatusLabel.IsVisible = true;
    }
}
