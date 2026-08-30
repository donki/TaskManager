using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace TaskManager.Core.Services;

/// <inheritdoc cref="IMailReader"/>
/// <remarks>
/// MailKit (MIT) sobre IMAP, hablando **desde el propio dispositivo** con el servidor de correo.
/// Del mensaje solo se trae la cabecera y el principio del cuerpo: lo justo para decidir si merece
/// una tarea, sin descargar adjuntos ni guardar el correo en ningun sitio.
/// </remarks>
public sealed class MailKitReader : IMailReader
{
    /// <summary>Caracteres de cuerpo que se traen para la vista previa.</summary>
    private const int PreviewLength = 400;

    public async Task<IReadOnlyList<MailMessage>> FetchAsync(
        MailAccount account,
        string secret,
        int take = 25,
        bool onlyUnread = false,
        bool useOAuth = false,
        CancellationToken cancellationToken = default)
    {
        if (!account.IsComplete)
        {
            throw new MailException("Faltan datos de la cuenta de correo.");
        }

        using var client = new ImapClient();

        try
        {
            await client.ConnectAsync(
                account.ImapHost,
                account.ImapPort,
                account.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
                cancellationToken).ConfigureAwait(false);

            if (useOAuth)
            {
                // XOAUTH2: es el unico modo que aceptan ya Gmail y Microsoft 365 para IMAP.
                var sasl = new SaslMechanismOAuth2(account.Address, secret);
                await client.AuthenticateAsync(sasl, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await client.AuthenticateAsync(account.Address, secret, cancellationToken).ConfigureAwait(false);
            }

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);

            var uids = onlyUnread
                ? await inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken).ConfigureAwait(false)
                : await inbox.SearchAsync(SearchQuery.All, cancellationToken).ConfigureAwait(false);

            // Los mas recientes primero, y solo los que se van a enseñar: un buzon con miles de
            // correos no puede convertirse en miles de descargas.
            var wanted = uids.Reverse().Take(take).ToList();
            if (wanted.Count == 0)
            {
                return [];
            }

            var summaries = await inbox
                .FetchAsync(wanted, MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.UniqueId,
                    cancellationToken)
                .ConfigureAwait(false);

            var messages = new List<MailMessage>(summaries.Count);

            foreach (var summary in summaries.OrderByDescending(s => s.Date))
            {
                var preview = await ReadPreviewAsync(inbox, summary.UniqueId, cancellationToken).ConfigureAwait(false);

                messages.Add(new MailMessage(
                    Uid: summary.UniqueId.Id,
                    From: summary.Envelope?.From?.ToString() ?? "(sin remitente)",
                    Subject: summary.Envelope?.Subject ?? string.Empty,
                    Preview: preview,
                    Date: summary.Date,
                    IsUnread: summary.Flags?.HasFlag(MessageFlags.Seen) != true));
            }

            await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
            return messages;
        }
        catch (AuthenticationException ex)
        {
            // El fallo mas comun con diferencia, y el que mas confunde: merece un mensaje propio.
            throw new MailException(useOAuth
                ? "El servidor ha rechazado el token. Vuelve a entrar con la cuenta."
                : "El servidor ha rechazado el usuario o la contraseña. Gmail necesita una contraseña " +
                  "de aplicación, y Outlook.com ya no admite contraseña para IMAP: entra con la cuenta.", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MailException($"No se ha podido leer el buzón: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Primeras lineas del mensaje. Si el cuerpo no se puede leer (cifrado, formato raro), se
    /// devuelve vacio: la vista previa es una ayuda, no un requisito.
    /// </summary>
    private static async Task<string> ReadPreviewAsync(
        IMailFolder inbox,
        UniqueId uid,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = await inbox.GetMessageAsync(uid, cancellationToken).ConfigureAwait(false);
            var text = message.TextBody ?? message.HtmlBody ?? string.Empty;

            if (text.Length == 0)
            {
                return string.Empty;
            }

            // Se compacta el espacio en blanco: los correos vienen llenos de saltos de linea que en
            // una vista previa solo estorban.
            var compact = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return compact.Length <= PreviewLength ? compact : compact[..PreviewLength] + "...";
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
