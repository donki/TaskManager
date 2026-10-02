using System.Net;
using TaskManager.Core.Services;
using TaskManager.Mobile.Pages;
using TaskManager.Mobile.Services;
using TaskManager.Mobile.Tests.Infra;
using TaskManager.Tests;

namespace TaskManager.Mobile.Tests;

/// <summary>La puerta de entrada y los ajustes (cuenta, avisos, celebracion, nombre).</summary>
public class AccountPagesTests
{
    // -----------------------------------------------------------------------
    // Entrada
    // -----------------------------------------------------------------------

    /// <summary>Un almacen de tokens que no deja guardar: la entrada falla por dentro.</summary>
    private sealed class BrokenTokens : ITokenStore
    {
        public Task<string?> GetAsync(string key) => Task.FromResult<string?>(null);

        public Task SetAsync(string key, string? value) => throw new IOException("almacen roto");
    }

    [Fact]
    public void Sin_sesion_la_puerta_se_queda_con_sus_botones() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync(account: "");
        var page = new LoginPage();
        await page.Appear();

        Assert.Empty(app.Ui.Routes);
        Assert.False(page.Named<ActivityIndicator>("Busy").IsRunning);
        Assert.True(page.Named<Button>("GoogleButton").IsEnabled);
        Assert.True(page.Named<Button>("LocalButton").IsVisible);
        Assert.True(page.Named<Button>("MicrosoftButton").IsVisible);
    });

    [Fact]
    public void Con_sesion_guardada_se_aparta_sola() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync(account: "");
        await app.Auth.SignInLocallyAsync();

        var page = new LoginPage();
        await page.Appear();

        Assert.Equal(["//MyTasksPage"], app.Ui.Routes);
    });

    [Fact]
    public void Entrar_sin_cuenta_adopta_lo_de_antes_y_va_a_mis_tareas() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync(account: "");
        var page = new LoginPage();

        await page.Handler("OnLocalClicked");

        Assert.Equal(["//MyTasksPage"], app.Ui.Routes);
        Assert.True(app.Auth.IsLocalAccount);
        Assert.StartsWith("local-", app.Repository.AccountId);
        Assert.False(page.Named<ActivityIndicator>("Busy").IsRunning);

        // Si algo falla por dentro se dice, y los botones vuelven.
        var broken = new LoginPage(new SupabaseAuthService(app.Http.Client(), app.Settings, new BrokenTokens(), app.Browser),
            app.Tasks, app.Settings);
        await broken.Handler("OnLocalClicked");
        Assert.Equal($"{app.Texts["SignInFailed"]}: almacen roto", broken.Named<Label>("StatusLabel").Text);
        Assert.True(broken.Named<Label>("StatusLabel").IsVisible);
        Assert.True(broken.Named<Button>("LocalButton").IsEnabled);
    });

    [Fact]
    public void Entrar_con_cuenta_cancelada_rechazada_o_con_fallo_lo_cuenta() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync(account: "");
        var page = new LoginPage();
        var configured = app.Auth.IsConfiguredFor(IdentityProvider.Google);

        // Cerrar la pestaña sin terminar.
        app.Browser.Respond = _ => Task.FromException<Uri>(new OperationCanceledException());
        await page.Handler("OnGoogleClicked");
        var status = page.Named<Label>("StatusLabel");
        Assert.True(status.IsVisible);
        if (configured)
        {
            Assert.Equal(app.Texts["SignInCancelled"], status.Text);

            // Un fallo cualquiera.
            app.Browser.Respond = _ => Task.FromException<Uri>(new InvalidOperationException("sin navegador"));
            await page.Handler("OnMicrosoftClicked");
            Assert.Equal($"{app.Texts["SignInFailed"]}: sin navegador", status.Text);

            // El proveedor dice que no: AuthException con su mensaje.
            app.Browser.Respond = _ => Task.FromResult(new Uri("com.socratic.taskmanager://auth?error=access_denied"));
            await page.Handler("OnGoogleClicked");
            Assert.NotEqual($"{app.Texts["SignInFailed"]}: sin navegador", status.Text);
        }

        Assert.Empty(app.Ui.Routes);
        Assert.True(page.Named<Button>("GoogleButton").IsEnabled);
    });

    [Fact]
    public void Entrar_con_Google_de_verdad_contra_el_servidor_falso() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync(account: "");
        if (!app.Auth.IsConfiguredFor(IdentityProvider.Google))
        {
            return;   // compilacion sin oauth.local.props: no hay cliente con el que entrar
        }

        app.Http.OnJson(HttpMethod.Post, "https://oauth2.googleapis.com/token", new
        {
            id_token = Jwt.Make(new { sub = "sub-123", email = "ana@example.com", name = "Ana" }),
            refresh_token = "refresco",
            expires_in = 3600,
        });
        app.Http.On(HttpMethod.Post, "/auth/v1/token", HttpStatusCode.BadRequest, "sin servidor");

        var page = new LoginPage();
        await page.Handler("OnGoogleClicked");

        Assert.Equal(["//MyTasksPage"], app.Ui.Routes);
        Assert.Equal("ana@example.com", app.Auth.CurrentUser!.Email);
    });

    // -----------------------------------------------------------------------
    // Ajustes
    // -----------------------------------------------------------------------

    [Fact]
    public void Ajustes_sin_cuenta_y_con_la_cuenta_local() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync(account: "");
        await app.Settings.SetBoolAsync(SettingsService.KeyHaptics, false);
        await app.Settings.SetAsync(SettingsService.KeySnoozeMinutes, "30");
        await app.Settings.SetAsync(SettingsService.KeyDisplayName, "Ana");

        var page = new SettingsPage();
        await page.Appear();

        Assert.False(page.Named<Switch>("HapticsSwitch").IsToggled);
        Assert.True(page.Named<Switch>("NotifySwitch").IsToggled);
        Assert.Equal(TimeSpan.FromHours(9), page.Named<TimePicker>("NotifyTimePicker").Time);
        Assert.Equal("Ana", page.Named<Entry>("DisplayNameEntry").Text);
        Assert.Equal(2, page.Named<Picker>("SnoozePicker").SelectedIndex);
        Assert.Equal(
            [app.Texts["SnoozeOff"], app.Texts.Format("SnoozeMinutes", 15), app.Texts.Format("SnoozeMinutes", 30),
             app.Texts["SnoozeHour"], app.Texts.Format("SnoozeHours", 2), app.Texts.Format("SnoozeHours", 4)],
            (List<string>)page.Named<Picker>("SnoozePicker").ItemsSource);

        Assert.Equal(app.Texts["NoAccount"], page.Named<Label>("AccountNameLabel").Text);
        Assert.Equal(app.Texts["NoAccountMobile"], page.Named<Label>("AccountEmailLabel").Text);
        Assert.False(page.Named<Button>("SignOutButton").IsVisible);
        Assert.Equal(app.Texts["AccountListsHint"], page.Named<Label>("AccountHintLabel").Text);
        Assert.False(page.Named<VisualElement>("AvatarFrame").IsVisible);

        // Cuenta local: se dice que es de este dispositivo, y salir no pregunta.
        await app.Auth.SignInLocallyAsync();
        await page.Appear();
        Assert.Equal(app.Texts["LocalAccount"], page.Named<Label>("AccountNameLabel").Text);
        Assert.Equal(app.Texts["LocalAccountDetail"], page.Named<Label>("AccountEmailLabel").Text);
        Assert.Equal(app.Texts["SignInWithAccount"], page.Named<Button>("SignOutButton").Text);
        Assert.Equal(app.Texts["LocalAccountHint"], page.Named<Label>("AccountHintLabel").Text);

        await page.Named<Button>("SignOutButton").Click();
        Assert.Empty(app.Ui.Dialogs);
        Assert.False(app.Auth.IsSignedIn);
        Assert.Equal(["//LoginPage"], app.Ui.Routes);
    });

    [Fact]
    public void Ajustes_con_cuenta_de_verdad_y_salir_preguntando() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync(account: "sub-ana");
        await app.SignInStoredAsync();

        var page = new SettingsPage();
        await page.Appear();

        Assert.Equal("Ana", page.Named<Label>("AccountNameLabel").Text);
        Assert.Equal("ana@example.com · Microsoft", page.Named<Label>("AccountEmailLabel").Text);
        Assert.True(page.Named<VisualElement>("AvatarFrame").IsVisible);
        Assert.NotNull(page.Named<Image>("AvatarImage").Source);
        Assert.Equal(app.Texts["SignOut"], page.Named<Button>("SignOutButton").Text);
        Assert.Equal(app.Texts["SwitchAccountHint"], page.Named<Label>("AccountHintLabel").Text);

        // Un proveedor raro guardado se lee como Google.
        await app.Settings.SetAsync(SettingsService.KeyAuthProvider, "Otro");
        Assert.Equal(IdentityProvider.Google, await page.Call("CurrentProvider"));

        // Cancelar: sigue dentro.
        await page.Named<Button>("SignOutButton").Click();
        Assert.True(app.Auth.IsSignedIn);
        Assert.Empty(app.Ui.Routes);

        app.Ui.Answer(true);
        await page.Named<Button>("SignOutButton").Click();
        Assert.False(app.Auth.IsSignedIn);
        Assert.Equal(["//LoginPage"], app.Ui.Routes);
        Assert.Null(await page.Call("CurrentProvider"));
    });

    [Fact]
    public void Los_avisos_se_encienden_con_permiso_y_se_reprograman() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var page = new SettingsPage();
        await page.Appear();
        var notify = page.Named<Switch>("NotifySwitch");

        // Apagar: se guarda, se cancela el diario y se esconden las filas.
        await PageDriver.Set(() => notify.IsToggled = false);
        Assert.False(app.Settings.NotificationsEnabled);
        Assert.Contains(ReminderScheduler.DailyRequestCode, app.Reminders.Cancelled);
        Assert.False(page.Named<VisualElement>("NotifyHourRow").IsVisible);
        Assert.False(page.Named<VisualElement>("SnoozeRow").IsVisible);

        // Cambiar la hora con los avisos apagados no hace nada.
        await PageDriver.Set(() => page.Named<TimePicker>("NotifyTimePicker").Time = TimeSpan.FromHours(7));
        Assert.Equal(9, app.Settings.NotifyHour);

        // Encender sin permiso: el interruptor vuelve a su sitio y se explica.
        app.Reminders.GrantOnRequest = false;
        await PageDriver.Set(() => notify.IsToggled = true);
        Assert.False(notify.IsToggled);
        Assert.False(app.Settings.NotificationsEnabled);
        Assert.Equal("Sin permiso", app.Ui.Dialogs.Single().Title);

        // Con permiso: se guarda y se programa a la hora puesta.
        app.Reminders.GrantOnRequest = true;
        await PageDriver.Set(() => notify.IsToggled = true);
        Assert.True(app.Settings.NotificationsEnabled);
        Assert.Equal(7, app.Reminders.Scheduled[ReminderScheduler.DailyRequestCode].Moment.Hour);

        // Cambiar la hora reprograma.
        await PageDriver.Set(() => page.Named<TimePicker>("NotifyTimePicker").Time = TimeSpan.FromHours(20));
        Assert.Equal(20, app.Settings.NotifyHour);
        Assert.Equal(20, app.Reminders.Scheduled[ReminderScheduler.DailyRequestCode].Moment.Hour);
        await page.Call("OnNotifyTimeChanged", null, new System.ComponentModel.PropertyChangedEventArgs("Otra"));

        // Repetir el aviso.
        await PageDriver.Set(() => page.Named<Picker>("SnoozePicker").SelectedIndex = 3);
        Assert.Equal(60, app.Settings.SnoozeMinutes);
    });

    [Fact]
    public void Celebracion_y_nombre_se_guardan() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var page = new SettingsPage();
        await page.Appear();

        await PageDriver.Set(() => page.Named<Switch>("HapticsSwitch").IsToggled = false);
        await PageDriver.Set(() => page.Named<Switch>("SoundSwitch").IsToggled = !page.Named<Switch>("SoundSwitch").IsToggled);
        Assert.False(app.Settings.HapticsEnabled);
        Assert.Equal(page.Named<Switch>("SoundSwitch").IsToggled, app.Settings.SoundEnabled);

        page.Named<Entry>("DisplayNameEntry").Text = "  Ana Sola ";
        await page.Handler("OnSaveName");
        Assert.Equal("Ana Sola", app.Settings.DisplayName);

        // Mientras se rellenan, los interruptores no escriben.
        page.SetField("_loading", true);
        await page.Call("OnHapticsToggled", null, new ToggledEventArgs(true));
        await page.Call("OnSoundToggled", null, new ToggledEventArgs(true));
        await page.Call("OnNotifyToggled", null, new ToggledEventArgs(false));
        Assert.False(app.Settings.HapticsEnabled);
        Assert.True(app.Settings.NotificationsEnabled);
    });
}
