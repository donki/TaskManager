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
    /// Esta pagina es la primera ruta del Shell y hace de puerta. Solo se aparta cuando hay cuenta:
    /// la entrada es obligatoria (<see cref="AuthOptions"/>), asi que no hay ninguna otra salida.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _settings.LoadAsync();

        SetBusy(true);
        var restored = await _auth.RestoreSessionAsync();
        SetBusy(false);

        if (restored is not null)
        {
            await Shell.Current.GoToAsync("//MyTasksPage");
        }
    }

    private async void OnGoogleClicked(object? sender, EventArgs e) =>
        await SignInAsync(IdentityProvider.Google);

    private async void OnMicrosoftClicked(object? sender, EventArgs e) =>
        await SignInAsync(IdentityProvider.Microsoft);

    private async Task SignInAsync(IdentityProvider provider)
    {
        SetBusy(true);
        try
        {
            var user = await _auth.SignInAsync(provider);

            // Lo hecho antes de entrar pasa a la cuenta: el nivel y las rachas no se pierden.
            await _tasks.AdoptAccountAsync(user.Id);

            await Shell.Current.GoToAsync("//MyTasksPage");
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

    private void SetBusy(bool busy)
    {
        Busy.IsRunning = busy;
        Busy.IsVisible = busy;
        GoogleButton.IsEnabled = !busy;

        // Microsoft esta oculto de momento (AuthOptions.MicrosoftSignInEnabled): el flujo esta
        // escrito y probado, pero no se ofrece hasta que se compruebe de verdad en un dispositivo.
        MicrosoftButton.IsVisible = TaskManager.Core.AuthOptions.MicrosoftSignInEnabled;
        MicrosoftButton.IsEnabled = !busy;
    }

    private void ShowStatus(string text)
    {
        StatusLabel.Text = text;
        StatusLabel.IsVisible = true;
    }
}
