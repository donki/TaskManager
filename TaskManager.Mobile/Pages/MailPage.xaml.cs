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

    private readonly List<MailRow> _rows = [];

    public MailPage()
        : this(ServiceHelper.GetRequiredService<IMailReader>(),
               ServiceHelper.GetRequiredService<TaskService>(),
               ServiceHelper.GetRequiredService<SettingsService>(),
               ServiceHelper.GetRequiredService<ITokenStore>())
    {
    }

    public MailPage(IMailReader mail, TaskService tasks, SettingsService settings, ITokenStore tokens)
    {
        InitializeComponent();

        _mail = mail;
        _tasks = tasks;
        _settings = settings;
        _tokens = tokens;

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

        ShowProviderHint();
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

        if (!address.Contains('@') || password.Length == 0)
        {
            StatusLabel.Text = "Faltan la dirección o la contraseña.";
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
        await _tokens.SetAsync(PasswordKey, password);

        ConnectButton.IsEnabled = false;
        StatusLabel.Text = "Leyendo el buzón...";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var messages = await _mail.FetchAsync(account, password, take: 25, cancellationToken: cts.Token);

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
            ?? (await _tasks.Repository.CreateListAsync("Tareas")).Id;

        var task = await _tasks.Repository.AddTaskAsync(listId, row.Message.ToTaskTitle(), inMyDay: true);
        task.Context = row.Message.ToTaskContext();
        task.Tags = TaskManager.Core.Models.TaskTags.FromInput("correo");
        await _tasks.Repository.UpdateTaskAsync(task);

        StatusLabel.Text = $"Tarea creada: {task.Title}";
    }
}
