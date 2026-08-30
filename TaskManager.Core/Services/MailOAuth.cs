using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TaskManager.Core.Services;

/// <summary>
/// Proveedor de correo con OAuth2: a donde se manda al usuario y que permisos se piden.
/// </summary>
public sealed record MailOAuthProvider(
    string Name,
    string AuthorizeUrl,
    string TokenUrl,
    string Scopes,
    string ImapHost,
    int ImapPort)
{
    /// <summary>
    /// Microsoft (Outlook.com, Microsoft 365). <c>offline_access</c> es lo que permite renovar sin
    /// volver a molestar al usuario; <c>IMAP.AccessAsUser.All</c> es el permiso que exige el IMAP de
    /// Microsoft, distinto del <c>Mail.Read</c> de Graph.
    /// </summary>
    public static readonly MailOAuthProvider Microsoft = new(
        "Microsoft",
        "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
        "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        "offline_access https://outlook.office.com/IMAP.AccessAsUser.All",
        "outlook.office365.com",
        993);

    /// <summary>Google (Gmail). <c>gmail.readonly</c> es ambito restringido: exige verificacion.</summary>
    public static readonly MailOAuthProvider Google = new(
        "Google",
        "https://accounts.google.com/o/oauth2/v2/auth",
        "https://oauth2.googleapis.com/token",
        "https://mail.google.com/",
        "imap.gmail.com",
        993);
}

/// <summary>
/// Identificadores de cliente de cada proveedor.
/// </summary>
/// <remarks>
/// <para>Se crean <b>una sola vez</b> y no vuelven a tocarse:</para>
/// <list type="bullet">
/// <item><b>Microsoft</b>: registro de aplicacion multi-tenant (Entra ID), cliente publico, con las
/// redirecciones de esta aplicacion. Da el <c>client_id</c>.</item>
/// <item><b>Google</b>: ID de cliente OAuth en Google Cloud Console (solo consola: no hay API ni
/// CLI que los cree).</item>
/// </list>
/// <para>Lo que si es interactivo, y es lo que hace esta aplicacion, es todo lo demas: el usuario
/// entra con su cuenta, y si su organizacion exige aprobacion, su administrador ve la pantalla de
/// consentimiento y al aceptar Entra crea el Service Principal (Enterprise Application) en su
/// tenant. Ver <see cref="MailOAuthService.BuildAdminConsentUrl"/>.</para>
/// <para>Vacio = ese proveedor no se ofrece; la aplicacion sigue funcionando con IMAP y contraseña
/// de aplicacion.</para>
/// </remarks>
public static class MailOAuthConfig
{
    // Los valores salen de OAuthSecrets, que genera oauth.props al compilar a partir de
    // oauth.local.props o de variables de entorno. En el repositorio no hay ningun identificador
    // ni secreto: GitHub los rechaza y la constitucion tampoco los admite.

    /// <summary>Registro multi-tenant en Entra ID.</summary>
    public static string MicrosoftClientId => OAuthSecrets.MicrosoftClientId;

    /// <summary>
    /// Google exige un cliente por plataforma: el de Android se valida por paquete y huella (y no
    /// lleva secreto), y el de escritorio por la redireccion a localhost (y si lo lleva).
    /// </summary>
    public static string GoogleAndroidClientId => OAuthSecrets.GoogleAndroidClientId;

    public static string GoogleDesktopClientId => OAuthSecrets.GoogleDesktopClientId;

    /// <summary>
    /// "Secreto" del cliente de escritorio. En una aplicacion instalada no es secreto de verdad
    /// —viaja dentro del binario y cualquiera puede extraerlo—, pero Google lo exige igualmente en
    /// el intercambio del codigo. Por eso el flujo se apoya en PKCE, que es lo que de verdad impide
    /// que un tercero use un codigo robado.
    /// </summary>
    public static string GoogleDesktopClientSecret => OAuthSecrets.GoogleDesktopClientSecret;

    public static bool IsConfigured(MailOAuthProvider provider) => ClientIdFor(provider).Length > 0;

    public static string ClientIdFor(MailOAuthProvider provider) =>
        provider.Name == "Microsoft"
            ? MicrosoftClientId
            : (OperatingSystem.IsAndroid() ? GoogleAndroidClientId : GoogleDesktopClientId);

    /// <summary>Secreto que acompaña al intercambio, si el proveedor lo exige. Vacio si no.</summary>
    public static string ClientSecretFor(MailOAuthProvider provider) =>
        provider.Name == "Google" && !OperatingSystem.IsAndroid() ? GoogleDesktopClientSecret : string.Empty;
}

/// <summary>Sesion OAuth de un buzon: lo que hace falta para hablar IMAP y para renovarla.</summary>
public sealed record MailOAuthSession(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt)
{
    public bool IsExpired => ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(2);
}

/// <summary>
/// Entrada con Google o Microsoft para leer el correo, con el navegador del sistema y PKCE.
/// </summary>
/// <remarks>
/// Mismo patron que la entrada de Supabase: cliente publico sin secreto (no hay donde guardarlo en
/// una app de escritorio o movil), codigo de autorizacion con PKCE y token de refresco guardado en
/// el almacen seguro del dispositivo.
/// </remarks>
public sealed class MailOAuthService
{
    private readonly HttpClient _http;
    private readonly IOAuthBrowser _browser;
    private readonly ITokenStore _tokens;

    public MailOAuthService(HttpClient http, IOAuthBrowser browser, ITokenStore tokens)
    {
        _http = http;
        _browser = browser;
        _tokens = tokens;
    }

    /// <summary>
    /// URL de consentimiento del administrador para una organizacion. Es el flujo que aprovisiona la
    /// Enterprise Application en el tenant del cliente: el administrador entra, ve los permisos que
    /// se piden y, al aceptar para su organizacion, Entra crea el Service Principal y devuelve el
    /// control a la aplicacion con <c>admin_consent=True</c>.
    /// </summary>
    /// <param name="tenant">
    /// <c>organizations</c> para cualquier empresa, <c>common</c> para empresa o cuenta personal, o
    /// el dominio o identificador del tenant concreto.
    /// </param>
    public string BuildAdminConsentUrl(MailOAuthProvider provider, string tenant = "organizations")
    {
        if (provider.Name != "Microsoft")
        {
            throw new NotSupportedException("El consentimiento de administrador por URL es de Entra ID.");
        }

        return $"https://login.microsoftonline.com/{tenant}/v2.0/adminconsent" +
               $"?client_id={MailOAuthConfig.ClientIdFor(provider)}" +
               $"&scope={Uri.EscapeDataString(provider.Scopes)}" +
               $"&redirect_uri={Uri.EscapeDataString(_browser.RedirectUri)}";
    }

    /// <summary>
    /// Abre el navegador, deja que el usuario entre con su cuenta y devuelve la sesion. Si su
    /// organizacion exige aprobacion del administrador, es Entra quien lo lleva por esa pantalla:
    /// la aplicacion no tiene que hacer nada distinto.
    /// </summary>
    public async Task<MailOAuthSession> SignInAsync(
        MailOAuthProvider provider,
        CancellationToken cancellationToken = default)
    {
        var clientId = MailOAuthConfig.ClientIdFor(provider);
        if (clientId.Length == 0)
        {
            throw new MailException(
                $"Falta el identificador de cliente de {provider.Name}. Se crea una sola vez en su " +
                "consola y se pega en MailOAuthConfig.");
        }

        var verifier = CreateVerifier();

        var authorize = new Uri(
            $"{provider.AuthorizeUrl}" +
            $"?client_id={clientId}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectFor(provider))}" +
            $"&scope={Uri.EscapeDataString(provider.Scopes)}" +
            $"&code_challenge={Challenge(verifier)}" +
            $"&code_challenge_method=S256" +
            // Google solo entrega token de refresco si se le piden los dos, y consent fuerza que lo
            // vuelva a dar cuando el usuario ya habia aceptado antes.
            (provider.Name == "Google" ? "&access_type=offline&prompt=consent" : string.Empty));

        var callback = await _browser.AuthenticateAsync(authorize, cancellationToken).ConfigureAwait(false);
        var code = ReadParameter(callback, "code");

        if (string.IsNullOrEmpty(code))
        {
            var error = ReadParameter(callback, "error_description") ?? ReadParameter(callback, "error");
            throw new MailException(error is null
                ? "La entrada no ha devuelto ningún código."
                : $"{provider.Name} ha rechazado la entrada: {error}");
        }

        var session = await ExchangeAsync(provider, clientId, code, verifier, cancellationToken).ConfigureAwait(false);
        await StoreAsync(provider, session).ConfigureAwait(false);
        return session;
    }

    /// <summary>Sesion guardada, renovandola si hace falta. Null si nunca se entro.</summary>
    public async Task<MailOAuthSession?> RestoreAsync(
        MailOAuthProvider provider,
        CancellationToken cancellationToken = default)
    {
        var refresh = await _tokens.GetAsync(RefreshKey(provider)).ConfigureAwait(false);
        var access = await _tokens.GetAsync(AccessKey(provider)).ConfigureAwait(false);

        if (string.IsNullOrEmpty(refresh) && string.IsNullOrEmpty(access))
        {
            return null;
        }

        var expires = DateTimeOffset.TryParse(
            await _tokens.GetAsync(ExpiresKey(provider)).ConfigureAwait(false), out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

        var session = new MailOAuthSession(access ?? string.Empty, refresh, expires);
        if (!session.IsExpired || string.IsNullOrEmpty(refresh))
        {
            return session;
        }

        var renewed = await RefreshAsync(provider, refresh, cancellationToken).ConfigureAwait(false);
        await StoreAsync(provider, renewed).ConfigureAwait(false);
        return renewed;
    }

    public async Task SignOutAsync(MailOAuthProvider provider)
    {
        await _tokens.SetAsync(AccessKey(provider), null).ConfigureAwait(false);
        await _tokens.SetAsync(RefreshKey(provider), null).ConfigureAwait(false);
        await _tokens.SetAsync(ExpiresKey(provider), null).ConfigureAwait(false);
    }

    // ==================================================================================

    private async Task<MailOAuthSession> ExchangeAsync(
        MailOAuthProvider provider,
        string clientId,
        string code,
        string verifier,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectFor(provider),
            ["code_verifier"] = verifier,
        };

        AddSecret(provider, form);
        return await PostTokenAsync(provider, form, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MailOAuthSession> RefreshAsync(
        MailOAuthProvider provider,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = MailOAuthConfig.ClientIdFor(provider),
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };

        AddSecret(provider, form);

        // Algunos proveedores no reenvian el token de refresco al renovar: se conserva el que habia.
        return await PostTokenAsync(provider, form, refreshToken, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MailOAuthSession> PostTokenAsync(
        MailOAuthProvider provider,
        Dictionary<string, string> form,
        string? fallbackRefresh,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(provider.TokenUrl, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new MailException($"{provider.Name} rechazó la sesión ({(int)response.StatusCode}): {body}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        if (string.IsNullOrEmpty(access))
        {
            throw new MailException($"{provider.Name} no devolvió ningún token de acceso.");
        }

        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : fallbackRefresh;
        var seconds = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;

        return new MailOAuthSession(access, refresh, DateTimeOffset.UtcNow.AddSeconds(seconds));
    }

    private async Task StoreAsync(MailOAuthProvider provider, MailOAuthSession session)
    {
        await _tokens.SetAsync(AccessKey(provider), session.AccessToken).ConfigureAwait(false);
        await _tokens.SetAsync(RefreshKey(provider), session.RefreshToken).ConfigureAwait(false);
        await _tokens.SetAsync(ExpiresKey(provider), session.ExpiresAt.ToString("O")).ConfigureAwait(false);
    }

    private static void AddSecret(MailOAuthProvider provider, Dictionary<string, string> form)
    {
        var secret = MailOAuthConfig.ClientSecretFor(provider);
        if (secret.Length > 0)
        {
            form["client_secret"] = secret;
        }
    }

    /// <summary>
    /// A donde vuelve el proveedor. Google no admite en Android un esquema cualquiera: el cliente de
    /// tipo Android espera el identificador invertido (<c>com.googleusercontent.apps.ID:/oauth2redirect</c>),
    /// comprobado el 2026-08-30 — con el esquema propio de la aplicacion responde
    /// <c>Error 400: invalid_request</c>. Microsoft si acepta el esquema propio.
    /// </summary>
    private string RedirectFor(MailOAuthProvider provider)
    {
        if (provider.Name == "Google" && OperatingSystem.IsAndroid())
        {
            var id = MailOAuthConfig.GoogleAndroidClientId.Replace(".apps.googleusercontent.com", string.Empty);
            return $"com.googleusercontent.apps.{id}:/oauth2redirect";
        }

        return _browser.RedirectUri;
    }

    private static string AccessKey(MailOAuthProvider p) => $"mail.{p.Name.ToLowerInvariant()}.access";

    private static string RefreshKey(MailOAuthProvider p) => $"mail.{p.Name.ToLowerInvariant()}.refresh";

    private static string ExpiresKey(MailOAuthProvider p) => $"mail.{p.Name.ToLowerInvariant()}.expires";

    private static string? ReadParameter(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == name)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(64));

    private static string Challenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
