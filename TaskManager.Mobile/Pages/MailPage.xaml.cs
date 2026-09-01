using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;

namespace TaskManager.Mobile.Pages;

/// <summary>Correo tal y como se pinta en la lista.</summary>
public sealed class MailRow
{
    public MailRow(MailMessage message)
    {
        Message = message;
        Uid = message.Uid;
    }

    public MailMessage Message { get; }

    public uint Uid { get; }

    public string Subject => Message.Subject.Length > 0 ? Message.Subject : "(sin asunto)";

    public string FromAndDate => $"{Message.From} · {Message.Date:d MMM HH:mm}";

    public string Preview => Message.Preview;

    /// <summary>Los sin leer, en negrita: es lo que distingue de un vistazo lo pendiente.</summary>
    public FontAttributes Weight => Message.IsUnread ? FontAttributes.Bold : FontAttributes.None;
}

/// <summary>
/// Buzon de correo: lee la bandeja de entrada por IMAP y convierte los mensajes que interesen en
/// tareas, con el remitente, la fecha y el principio del mensaje ya puestos como contexto.
/// </summary>
/// <remarks>
/// Todo ocurre en el dispositivo: la aplicacion habla directamente con el servidor de correo y no
/// hay ningun servidor intermedio. La contraseña se guarda en el almacen seguro de Android, nunca
/// en la base de datos.
/// </remarks>
public partial class MailPage : ContentPage
{
    private const string PasswordKey = "mail.password";

    private readonly IMailReader _mail;
    private readonly TaskService _tasks;
    private readonly SettingsService _settings;
    private readonly ITokenStore _tokens;
    private readonly MailOAuthService _oauth;

    /// <summary>Sesion OAuth en uso, si se entro con la cuenta en vez de con contraseña.</summary>
    private MailOAuthSession? _session;
    private MailOAuthProvider? _provider;

    private readonly List<MailRow> _rows = [];

    public MailPage()
        : this(ServiceHelper.GetRequiredService<IMailReader>(),
               ServiceHelper.GetRequiredService<TaskService>(),
               ServiceHelper.GetRequiredService<SettingsService>(),
               ServiceHelper.GetRequiredService<ITokenStore>(),
               ServiceHelper.GetRequiredService<MailOAuthService>())
    {
    }

    public MailPage(
        IMailReader mail,
        TaskService tasks,
        SettingsService settings,
        ITokenStore tokens,
        MailOAuthService oauth)
    {
        InitializeComponent();

        _mail = mail;
        _tasks = tasks;
        _settings = settings;
        _tokens = tokens;
        _oauth = oauth;

        MailView.ItemsSource = _rows;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _settings.LoadAsync();

        AddressEntry.Text = _settings.Get("mail.address");
        ImapHostEntry.Text = _settings.Get("mail.imap_host");
        ImapPortEntry.Text = _settings.Get("mail.imap_port", "993");
        PasswordEntry.Text = await _tokens.GetAsync(PasswordKey) ?? string.Empty;

        // Los botones de cuenta solo se ofrecen si hay identificador de cliente: sin el, pulsarlos
        // solo daria un error, asi que es mejor que no esten.
        GoogleButton.IsVisible = MailOAuthConfig.IsConfigured(MailOAuthProvider.Google);
        MicrosoftButton.IsVisible = MailOAuthConfig.IsConfigured(MailOAuthProvider.Microsoft);
        OAuthRow.IsVisible = GoogleButton.IsVisible || MicrosoftButton.IsVisible;

        // Sesion de una entrada anterior: si sigue viva, no hay que volver a pedir nada.
        foreach (var provider in new[] { MailOAuthProvider.Google, MailOAuthProvider.Microsoft })
        {
            if (!MailOAuthConfig.IsConfigured(provider))
            {
                continue;
            }

            var restored = await _oauth.RestoreAsync(provider);
            if (restored is not null)
            {
                _session = restored;
                _provider = provider;
                StatusLabel.Text = $"Sesión de {provider.Name} recuperada.";
                break;
            }
        }

        ShowProviderHint();
    }

    private async void OnGoogleSignInClicked(object? sender, EventArgs e) =>
        await SignInAsync(MailOAuthProvider.Google);

    private async void OnMicrosoftSignInClicked(object? sender, EventArgs e) =>
        await SignInAsync(MailOAuthProvider.Microsoft);

    /// <summary>
    /// Explica que ha fallado y, cuando toca, ofrece el consentimiento del administrador.
    /// </summary>
    /// <remarks>
    /// El consentimiento de organizacion es justo el flujo que se pidio: el administrador aprueba
    /// la aplicacion una vez y queda dada de alta en su directorio para todo el mundo. Solo tiene
    /// sentido ofrecerlo cuando el registro base existe; si no existe, primero hay que crearlo.
    /// </remarks>
    private async Task ShowOAuthProblemAsync(MailOAuthProvider provider, string problem)
    {
        var loc = Localization.Loc.Instance;

        var message = problem switch
        {
            "NoClientId" => loc["OAuthNoClientId"],
            "AppNotRegistered" => loc["OAuthAppNotRegistered"],
            _ => loc.Format("OAuthProviderError", problem),
        };

        if (problem == "AppNotRegistered" || provider.Name == "Google")
        {
            await SocShared.ModernDialog.AlertAsync(this, loc["SignInMicrosoft"], message, "OK");
            return;
        }

        // Se juntan en dos variables en vez de anidarlo todo en una interpolacion: con
        // comillas dentro de comillas no compila y se lee peor.
        var hint = loc["AdminConsentHint"];
        var detail = message + Environment.NewLine + Environment.NewLine + hint;

        var consent = await SocShared.ModernDialog.AlertAsync(this,
            loc["AdminConsent"], detail,
            loc["AdminConsent"], loc["Cancel"]);

        if (consent)
        {
            await Launcher.OpenAsync(new Uri(_oauth.BuildAdminConsentUrl(provider)));
        }
    }

    /// <summary>
    /// Abre el navegador para que el usuario entre con su cuenta. Si su organizacion exige
    /// aprobacion, es el propio proveedor quien le enseña la pantalla de consentimiento del
    /// administrador; la aplicacion no hace nada distinto.
    /// </summary>
    private async Task SignInAsync(MailOAuthProvider provider)
    {
        // Se comprueba el registro ANTES de abrir el navegador: si no existe, el proveedor enseña
        // su pagina de error y no vuelve nunca, y aqui no habria forma de contar que ha pasado.
        if (await _oauth.PreflightAsync(provider) is { } problem)
        {
            await ShowOAuthProblemAsync(provider, problem);
            return;
        }

        StatusLabel.Text = $"Entrando con {provider.Name}...";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            _session = await _oauth.SignInAsync(provider, cts.Token);
            _provider = provider;

            // Los servidores del proveedor mandan sobre lo que hubiera escrito a mano.
            ImapHostEntry.Text = provider.ImapHost;
            ImapPortEntry.Text = provider.ImapPort.ToString();

            StatusLabel.Text = $"Dentro con {provider.Name}. Pulsa «Leer buzón».";
        }
        catch (MailException ex)
        {
            StatusLabel.Text = string.Empty;
            await SocShared.ModernDialog.AlertAsync(this, "Correo", ex.Message, "OK");
        }
        catch (TaskCanceledException)
        {
            StatusLabel.Text = "Entrada cancelada.";
        }
    }

    /// <summary>
    /// Al escribir la direccion se rellenan los servidores del proveedor: nadie tiene por que
    /// saberse de memoria el host de IMAP de su correo.
    /// </summary>
    private void OnAddressCompleted(object? sender, EventArgs e)
    {
        var preset = MailProviders.ForAddress(AddressEntry.Text?.Trim() ?? string.Empty);

        if (preset.ImapHost.Length > 0)
        {
            ImapHostEntry.Text = preset.ImapHost;
            ImapPortEntry.Text = preset.ImapPort.ToString();
        }

        ShowProviderHint();
    }

    /// <summary>
    /// Se avisa por adelantado de lo que va a fallar: Gmail necesita contraseña de aplicacion y
    /// Outlook.com ya no admite contraseña para IMAP. Mejor decirlo antes que tras un error.
    /// </summary>
    private void ShowProviderHint()
    {
        var address = (AddressEntry.Text ?? string.Empty).ToLowerInvariant();

        ProviderHint.Text = address switch
        {
            _ when address.EndsWith("@gmail.com") || address.EndsWith("@googlemail.com") =>
                "Gmail: hace falta una contraseña de aplicación (con verificación en dos pasos activada), no la del correo.",
            _ when address.EndsWith("@outlook.com") || address.EndsWith("@hotmail.com") || address.EndsWith("@live.com") =>
                "Outlook.com ya no admite contraseña para IMAP: solo entra por OAuth2, que todavía no está implementado.",
            _ => "Con IMAP y contraseña de aplicación funcionan Gmail, Yahoo, iCloud, Zoho y cualquier servidor propio.",
        };
    }

    // ==================================================================================

    private async void OnConnectClicked(object? sender, EventArgs e) => await LoadMailAsync();

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadMailAsync();
        Refresher.IsRefreshing = false;
    }

    private async Task LoadMailAsync()
    {
        var address = AddressEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text ?? string.Empty;

        // Con sesion OAuth viva se usa el token; si no, la contraseña de aplicacion.
        var useOAuth = _session is not null && _provider is not null;
        if (useOAuth && _session!.IsExpired && _provider is not null)
        {
            _session = await _oauth.RestoreAsync(_provider);
        }

        var secret = useOAuth ? _session?.AccessToken ?? string.Empty : password;

        if (!address.Contains('@') || secret.Length == 0)
        {
            StatusLabel.Text = useOAuth
                ? "La sesión ha caducado: vuelve a entrar con la cuenta."
                : "Faltan la dirección o la contraseña.";
            return;
        }

        var account = MailProviders.ForAddress(address) with
        {
            ImapHost = ImapHostEntry.Text?.Trim() ?? string.Empty,
            ImapPort = int.TryParse(ImapPortEntry.Text, out var port) ? port : 993,
        };

        // Se guardan los datos antes de conectar: si la conexion falla, al menos no hay que
        // volver a escribirlo todo.
        await _settings.SetAsync("mail.address", address);
        await _settings.SetAsync("mail.imap_host", account.ImapHost);
        await _settings.SetAsync("mail.imap_port", account.ImapPort.ToString());
        if (!useOAuth)
        {
            await _tokens.SetAsync(PasswordKey, password);
        }

        ConnectButton.IsEnabled = false;
        StatusLabel.Text = Localization.Loc.Instance["MailReading"];

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var messages = await _mail.FetchAsync(
                account, secret, take: 25, useOAuth: useOAuth, cancellationToken: cts.Token);

            _rows.Clear();
            _rows.AddRange(messages.Select(m => new MailRow(m)));
            MailView.ItemsSource = null;
            MailView.ItemsSource = _rows;

            StatusLabel.Text = messages.Count == 0
                ? "No hay correos en la bandeja."
                : $"{messages.Count} correos · {messages.Count(m => m.IsUnread)} sin leer";

            AccountCard.IsVisible = messages.Count == 0;
        }
        catch (MailException ex)
        {
            StatusLabel.Text = string.Empty;
            await SocShared.ModernDialog.AlertAsync(this, "Correo", ex.Message, "OK");
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "El servidor ha tardado demasiado.";
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Convierte el correo en tarea de hoy: el asunto es el titulo y el remitente, la fecha y el
    /// principio del mensaje quedan como contexto, que es lo que luego usa el desglose.
    /// </summary>
    private async void OnCreateTaskClicked(object? sender, EventArgs e)
    {
        if (sender is not ImageButton { CommandParameter: uint uid })
        {
            return;
        }

        var row = _rows.FirstOrDefault(r => r.Uid == uid);
        if (row is null)
        {
            return;
        }

        var lists = await _tasks.Repository.GetPrivateListsAsync();
        var listId = lists.FirstOrDefault()?.Id
            ?? (await _tasks.Repository.CreateListAsync(Localization.Loc.Instance["DefaultListName"])).Id;

        var task = await _tasks.Repository.AddTaskAsync(listId, row.Message.ToTaskTitle(), inMyDay: true);
        task.Notes = row.Message.ToTaskContext();
        task.Tags = TaskManager.Core.Models.TaskTags.FromInput("correo");
        await _tasks.Repository.UpdateTaskAsync(task);

        StatusLabel.Text = Localization.Loc.Instance.Format("TaskCreated", task.Title);
    }
}
