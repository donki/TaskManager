using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Pages;
using TaskManager.Mobile.Tests.Infra;

namespace TaskManager.Mobile.Tests;

/// <summary>El buzon (oculto de momento, pero en el codigo): leer el correo y convertirlo en tareas.</summary>
public class MailPageTests
{
    private static readonly MailMessage Unread = new(7, "Marta", "Factura", "Te mando la factura", DateTimeOffset.Now, true);
    private static readonly MailMessage Read = new(8, "Luis", "", "hola", DateTimeOffset.Now, false);

    private static List<MailRow> Rows(MailPage page) => (List<MailRow>)page.Named<CollectionView>("MailView").ItemsSource;

    [Fact]
    public void Rellena_lo_guardado_y_ofrece_las_cuentas_configuradas() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        await app.Settings.SetAsync("mail.address", "ana@example.com");
        await app.Settings.SetAsync("mail.imap_host", "imap.example.com");
        await app.Services.GetRequiredService<ITokenStore>().SetAsync("mail.password", "secreta");

        var page = new MailPage();
        await page.Appear();

        Assert.Equal("ana@example.com", page.Named<Entry>("AddressEntry").Text);
        Assert.Equal("imap.example.com", page.Named<Entry>("ImapHostEntry").Text);
        Assert.Equal("993", page.Named<Entry>("ImapPortEntry").Text);
        Assert.Equal("secreta", page.Named<Entry>("PasswordEntry").Text);
        Assert.Equal(MailOAuthConfig.IsConfigured(MailOAuthProvider.Google), page.Named<Button>("GoogleButton").IsVisible);
        Assert.StartsWith("Con IMAP", page.Named<Label>("ProviderHint").Text);
    });

    [Fact]
    public void La_direccion_rellena_el_servidor_y_avisa_de_lo_que_va_a_fallar() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var page = new MailPage();

        page.Named<Entry>("AddressEntry").Text = "ana@gmail.com";
        await page.Handler("OnAddressCompleted");
        Assert.Equal("imap.gmail.com", page.Named<Entry>("ImapHostEntry").Text);
        Assert.StartsWith("Gmail", page.Named<Label>("ProviderHint").Text);

        page.Named<Entry>("AddressEntry").Text = "ana@hotmail.com";
        await page.Handler("OnAddressCompleted");
        Assert.StartsWith("Outlook.com", page.Named<Label>("ProviderHint").Text);

        // Un dominio sin preset no toca el servidor escrito.
        page.Named<Entry>("ImapHostEntry").Text = "mio.example.com";
        page.Named<Entry>("AddressEntry").Text = "ana@example.com";
        await page.Handler("OnAddressCompleted");
        Assert.Equal("mio.example.com", page.Named<Entry>("ImapHostEntry").Text);
    });

    [Fact]
    public void Leer_el_buzon_con_contraseña() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var page = new MailPage();

        // Falta algo.
        await page.Handler("OnConnectClicked");
        Assert.Equal("Faltan la dirección o la contraseña.", page.Named<Label>("StatusLabel").Text);
        Assert.Empty(app.Mail.Calls);

        page.Named<Entry>("AddressEntry").Text = " ana@example.com ";
        page.Named<Entry>("PasswordEntry").Text = "app-pass";
        page.Named<Entry>("ImapHostEntry").Text = "imap.example.com";
        page.Named<Entry>("ImapPortEntry").Text = "no-es-numero";
        app.Mail.Messages.AddRange([Unread, Read]);
        await page.Handler("OnConnectClicked");

        var call = app.Mail.Calls.Single();
        Assert.Equal("app-pass", call.Secret);
        Assert.False(call.UseOAuth);
        Assert.Equal(993, call.Account.ImapPort);
        Assert.Equal("ana@example.com", app.Settings.Get("mail.address"));
        Assert.Equal("app-pass", await app.Services.GetRequiredService<ITokenStore>().GetAsync("mail.password"));
        Assert.Equal("2 correos · 1 sin leer", page.Named<Label>("StatusLabel").Text);
        Assert.False(page.Named<VisualElement>("AccountCard").IsVisible);
        Assert.True(page.Named<ImageButton>("ConnectButton").IsEnabled);

        var rows = Rows(page);
        Assert.Equal(FontAttributes.Bold, rows[0].Weight);
        Assert.Equal(FontAttributes.None, rows[1].Weight);
        Assert.Equal("(sin asunto)", rows[1].Subject);
        Assert.Equal("Factura", rows[0].Subject);
        Assert.StartsWith("Marta · ", rows[0].FromAndDate);
        Assert.Equal("Te mando la factura", rows[0].Preview);

        // Bandeja vacia; tirar hacia abajo vuelve a leer.
        app.Mail.Messages.Clear();
        page.Named<RefreshView>("Refresher").IsRefreshing = true;
        await UiThread.IdleAsync();
        Assert.Equal("No hay correos en la bandeja.", page.Named<Label>("StatusLabel").Text);
        Assert.True(page.Named<VisualElement>("AccountCard").IsVisible);
        Assert.False(page.Named<RefreshView>("Refresher").IsRefreshing);

        // Errores del servidor y servidor que no contesta.
        app.Mail.Fails = new MailException("contraseña incorrecta");
        await page.Handler("OnConnectClicked");
        Assert.Equal("contraseña incorrecta", app.Ui.Dialogs.Single().Message);
        Assert.Equal(string.Empty, page.Named<Label>("StatusLabel").Text);

        app.Mail.Fails = new OperationCanceledException();
        await page.Handler("OnConnectClicked");
        Assert.Equal("El servidor ha tardado demasiado.", page.Named<Label>("StatusLabel").Text);
        Assert.True(page.Named<ImageButton>("ConnectButton").IsEnabled);
    });

    [Fact]
    public void Un_correo_se_convierte_en_tarea_de_hoy() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var page = new MailPage();
        page.Named<Entry>("AddressEntry").Text = "ana@example.com";
        page.Named<Entry>("PasswordEntry").Text = "x";
        app.Mail.Messages.Add(Unread);
        await page.Handler("OnConnectClicked");

        await page.Handler("OnCreateTaskClicked", new Button());
        await page.Handler("OnCreateTaskClicked", PageDriver.RowButton(99u));
        Assert.Empty(await app.Repository.GetAllTasksAsync(TaskFilter.All));

        // Sin listas: va a la de por defecto.
        await page.Handler("OnCreateTaskClicked", PageDriver.RowButton(7u));
        var task = (await app.Repository.GetAllTasksAsync(TaskFilter.All)).Single();
        Assert.Equal("Factura", task.Title);
        Assert.Equal(Unread.ToTaskContext(), task.Notes);
        Assert.Equal(["correo"], TaskTags.Split(task.Tags));
        Assert.Equal(DateTime.Today, task.MyDayOn);
        Assert.Equal(app.Texts.Format("TaskCreated", "Factura"), page.Named<Label>("StatusLabel").Text);

        // Con listas: a la primera.
        await page.Handler("OnCreateTaskClicked", PageDriver.RowButton(7u));
        Assert.Single((await app.Repository.GetPrivateListsAsync()));
        Assert.Equal(2, (await app.Repository.GetAllTasksAsync(TaskFilter.All)).Count);
    });

    [Fact]
    public void Entrar_con_la_cuenta_de_Google_y_recuperar_la_sesion() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        if (!MailOAuthConfig.IsConfigured(MailOAuthProvider.Google))
        {
            return;   // compilacion sin oauth.local.props
        }

        var page = new MailPage();
        app.Http.OnJson(HttpMethod.Post, MailOAuthProvider.Google.TokenUrl,
            new { access_token = "acc", refresh_token = "ref", expires_in = 3600 });
        await page.Handler("OnGoogleSignInClicked");

        Assert.Equal("Dentro con Google. Pulsa «Leer buzón».", page.Named<Label>("StatusLabel").Text);
        Assert.Equal("imap.gmail.com", page.Named<Entry>("ImapHostEntry").Text);

        // Leer con el token, no con contraseña.
        page.Named<Entry>("AddressEntry").Text = "ana@gmail.com";
        await page.Handler("OnConnectClicked");
        Assert.Equal(("acc", true), (app.Mail.Calls.Single().Secret, app.Mail.Calls.Single().UseOAuth));

        // Otra pagina recupera la sesion sola.
        var again = new MailPage();
        await again.Appear();
        Assert.Equal("Sesión de Google recuperada.", again.Named<Label>("StatusLabel").Text);

        // Sesion caducada que no se puede renovar: se pide volver a entrar.
        page.SetField("_session", new MailOAuthSession("viejo", null, DateTimeOffset.UtcNow.AddHours(-1)));
        await app.Services.GetRequiredService<ITokenStore>().SetAsync("mail.google.refresh", null);
        await app.Services.GetRequiredService<ITokenStore>().SetAsync("mail.google.access", null);
        await page.Handler("OnConnectClicked");
        Assert.Equal("La sesión ha caducado: vuelve a entrar con la cuenta.", page.Named<Label>("StatusLabel").Text);
    });

    [Fact]
    public void Entrar_con_la_cuenta_rechazada_o_cancelada() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var page = new MailPage();

        if (!MailOAuthConfig.IsConfigured(MailOAuthProvider.Google))
        {
            // Sin cliente: el problema se explica antes de abrir nada.
            await page.Handler("OnGoogleSignInClicked");
            Assert.Equal(app.Texts["OAuthNoClientId"], app.Ui.Dialogs.Single().Message);
            return;
        }

        app.Browser.Respond = _ => Task.FromResult(new Uri("com.socratic.taskmanager://auth?error=access_denied"));
        await page.Handler("OnGoogleSignInClicked");
        Assert.Equal("Correo", app.Ui.Dialogs.Single().Title);
        Assert.Contains("access_denied", app.Ui.Dialogs.Single().Message);
        Assert.Equal(string.Empty, page.Named<Label>("StatusLabel").Text);

        app.Browser.Respond = _ => Task.FromException<Uri>(new TaskCanceledException());
        await page.Handler("OnGoogleSignInClicked");
        Assert.Equal("Entrada cancelada.", page.Named<Label>("StatusLabel").Text);
    });

    [Fact]
    public void Microsoft_sin_registro_o_con_aprobacion_del_administrador() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        if (!MailOAuthConfig.IsConfigured(MailOAuthProvider.Microsoft))
        {
            return;
        }

        var page = new MailPage();

        // El registro no existe: solo se explica.
        app.Http.On(HttpMethod.Post, "/devicecode", HttpStatusCode.BadRequest, "{\"error\":\"AADSTS700016: no existe.\"}");
        await page.Handler("OnMicrosoftSignInClicked");
        Assert.Equal(app.Texts["OAuthAppNotRegistered"], app.Ui.Dialogs.Single().Message);

        // La organizacion exige aprobacion: se ofrece el consentimiento; cancelar no abre nada.
        app.Http.On(HttpMethod.Post, "/devicecode", HttpStatusCode.BadRequest, "{\"error\":\"AADSTS65001: hace falta permiso.\"}");
        await page.Handler("OnMicrosoftSignInClicked");
        Assert.Equal(app.Texts["AdminConsent"], app.Ui.Dialogs.Last().Title);
        Assert.Contains(app.Texts.Format("OAuthProviderError", "AADSTS65001"), app.Ui.Dialogs.Last().Message);
        Assert.Empty(app.Ui.Opened);

        app.Ui.Answer(true);
        await page.Handler("OnMicrosoftSignInClicked");
        Assert.StartsWith("uri:https://login.microsoftonline.com/organizations/", app.Ui.Opened.Single());
    });
}
