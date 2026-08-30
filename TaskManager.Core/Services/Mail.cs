namespace TaskManager.Core.Services;

/// <summary>Un mensaje del buzon, con lo justo para decidir si merece una tarea.</summary>
public sealed record MailMessage(
    uint Uid,
    string From,
    string Subject,
    string Preview,
    DateTimeOffset Date,
    bool IsUnread)
{
    /// <summary>Titulo de tarea a partir del correo: el asunto, o el remitente si no tiene.</summary>
    public string ToTaskTitle() =>
        Subject.Trim().Length > 0 ? Subject.Trim() : $"Correo de {From}";

    /// <summary>
    /// Contexto de la tarea: de quien viene, cuando y las primeras lineas. Es justo lo que hace
    /// falta para desglosarla sin volver al buzon.
    /// </summary>
    public string ToTaskContext()
    {
        var lines = new List<string>
        {
            $"Correo de {From} ({Date:d MMM yyyy HH:mm}).",
        };

        if (Preview.Trim().Length > 0)
        {
            lines.Add(Preview.Trim());
        }

        return string.Join("\n\n", lines);
    }
}

/// <summary>
/// Datos de conexion de un buzon. La contraseña NO va aqui: se guarda en el almacen seguro del
/// dispositivo a traves de <see cref="ITokenStore"/>.
/// </summary>
public sealed record MailAccount(
    string Provider,
    string Address,
    string ImapHost,
    int ImapPort,
    string SmtpHost,
    int SmtpPort,
    bool UseSsl = true)
{
    public bool IsComplete => Address.Contains('@') && ImapHost.Length > 0;
}

/// <summary>
/// Proveedores habituales, para no tener que saberse los servidores de memoria.
/// </summary>
/// <remarks>
/// <para><b>Aviso sobre Gmail y Outlook.</b> Los dos siguen ofreciendo IMAP, pero ninguno acepta ya
/// la contraseña normal de la cuenta:</para>
/// <list type="bullet">
/// <item>Gmail exige una <b>contraseña de aplicacion</b> (requiere verificacion en dos pasos).</item>
/// <item>Outlook.com y Microsoft 365 desactivaron la autenticacion basica: solo entran por OAuth2,
/// asi que con usuario y contraseña **no van a conectar**. Para esas cuentas hace falta el registro
/// de aplicacion en Entra ID que quedo pendiente.</item>
/// </list>
/// <para>Con IMAP y contraseña de aplicacion funcionan hoy: Gmail, Yahoo, iCloud, Zoho y cualquier
/// servidor propio o de hosting.</para>
/// </remarks>
public static class MailProviders
{
    public static readonly IReadOnlyList<MailAccount> Presets =
    [
        new("Gmail", string.Empty, "imap.gmail.com", 993, "smtp.gmail.com", 587),
        new("Outlook / Hotmail", string.Empty, "outlook.office365.com", 993, "smtp.office365.com", 587),
        new("Yahoo", string.Empty, "imap.mail.yahoo.com", 993, "smtp.mail.yahoo.com", 587),
        new("iCloud", string.Empty, "imap.mail.me.com", 993, "smtp.mail.me.com", 587),
        new("Zoho", string.Empty, "imap.zoho.eu", 993, "smtp.zoho.eu", 587),
        new("Otro (IMAP/SMTP)", string.Empty, string.Empty, 993, string.Empty, 587),
    ];

    /// <summary>Preajuste que corresponde a una direccion, mirando su dominio.</summary>
    public static MailAccount ForAddress(string address)
    {
        var domain = address.Contains('@') ? address[(address.IndexOf('@') + 1)..].ToLowerInvariant() : string.Empty;

        return domain switch
        {
            "gmail.com" or "googlemail.com" => Presets[0] with { Address = address },
            "outlook.com" or "hotmail.com" or "live.com" or "msn.com" => Presets[1] with { Address = address },
            "yahoo.com" or "yahoo.es" => Presets[2] with { Address = address },
            "icloud.com" or "me.com" => Presets[3] with { Address = address },
            "zoho.com" or "zoho.eu" => Presets[4] with { Address = address },
            _ => Presets[5] with { Address = address },
        };
    }
}

/// <summary>Lee el buzon por IMAP. Todo ocurre en el dispositivo: no hay servidor de por medio.</summary>
public interface IMailReader
{
    /// <summary>
    /// Ultimos mensajes de la bandeja de entrada, mas recientes primero. Lanza
    /// <see cref="MailException"/> con un mensaje entendible si algo falla.
    /// </summary>
    /// <param name="secret">
    /// Contraseña de aplicacion, o token de acceso OAuth2 si <paramref name="useOAuth"/> es true.
    /// </param>
    Task<IReadOnlyList<MailMessage>> FetchAsync(
        MailAccount account,
        string secret,
        int take = 25,
        bool onlyUnread = false,
        bool useOAuth = false,
        CancellationToken cancellationToken = default);
}

public sealed class MailException(string message, Exception? inner = null) : Exception(message, inner);
