using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TaskManager.Core.Services;

/// <summary>Con que cuenta se entra.</summary>
public enum IdentityProvider
{
    Google,
    Microsoft,
}

/// <summary>
/// Quien ha entrado, tal y como lo cuenta su proveedor.
/// </summary>
/// <remarks>
/// <para><b><see cref="Subject"/> es la identidad.</b> Es el identificador de la cuenta: el mismo
/// numero en Windows, en Android y en cualquier aparato donde se entre con ella, y no cambia aunque
/// el usuario se cambie el nombre o el correo. Por eso es lo que se guarda como usuario de la
/// aplicacion, y no el correo —que se puede reasignar— ni un GUID por instalacion —que seria un
/// usuario distinto en cada equipo, justo lo contrario de lo que se busca—.</para>
///
/// <para><b>Sin prefijo de proveedor.</b> Se penso en guardarlo como <c>google:1159…</c> para que
/// dos proveedores no pudieran chocar, pero cambiar la forma del identificador deja huerfano todo
/// lo que ya se creo con el anterior —autoria, XP, rachas— y obliga a una migracion en cada aparato
/// a cambio de nada: el <c>sub</c> de Google son 21 digitos y el <c>oid</c> de Microsoft es un
/// UUID, asi que coincidir no pueden.</para>
///
/// <para>El <see cref="IdToken"/> se conserva porque es lo unico con lo que se puede demostrar ante
/// un tercero (Supabase) que esta identidad la firma el proveedor.</para>
/// </remarks>
public sealed record IdentityAccount(
    IdentityProvider Provider,
    string Subject,
    string Email,
    string Name,
    string Picture,
    string IdToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt)
{
    /// <summary>El identificador con el que la aplicacion conoce al usuario.</summary>
    public string UserId => Subject;
}

/// <summary>
/// Entrada con Google o con Microsoft, hablando <b>directamente con el proveedor</b>.
/// </summary>
/// <remarks>
/// <para><b>Por que no se pasa por Supabase.</b> Antes se abria
/// <c>/auth/v1/authorize?provider=google</c> y era Supabase quien reenviaba. Eso obliga a tener el
/// proveedor dado de alta en el proyecto, y mientras no lo este el navegador se planta en una pagina
/// de Supabase con un error. Yendo de frente, la entrada depende solo del cliente OAuth que la
/// aplicacion ya tiene y funciona con el proyecto en cualquier estado; la sesion del servidor se
/// consigue despues, canjeando el id_token.</para>
///
/// <para><b>PKCE.</b> El codigo vuelve como parametro de consulta, asi que sirve igual el servidor
/// local de Windows que el de Android. El "secreto" del cliente de escritorio de Google viaja dentro
/// del binario y no es secreto de verdad; lo que impide que sirva un codigo robado es PKCE. El
/// cliente de Microsoft es publico y no lleva ninguno.</para>
///
/// <para>Se reutilizan los clientes OAuth de <see cref="MailOAuthConfig"/>: son las mismas
/// aplicaciones registradas, y los ambitos de aqui —<c>openid email profile</c>— no son
/// restringidos, asi que no arrastran ninguna verificacion.</para>
/// </remarks>
public sealed class IdentitySignInService
{
    private readonly HttpClient _http;
    private readonly IOAuthBrowser _browser;

    public IdentitySignInService(HttpClient http, IOAuthBrowser browser)
    {
        _http = http;
        _browser = browser;
    }

    /// <summary>Los proveedores que esta compilacion puede ofrecer de verdad.</summary>
    public IReadOnlyList<IdentityProvider> Available =>
        [.. new[] { IdentityProvider.Google, IdentityProvider.Microsoft }.Where(IsConfigured)];

    public bool IsConfigured(IdentityProvider provider) => ClientId(provider).Length > 0;

    /// <summary>Abre el navegador del sistema y vuelve con la cuenta ya identificada.</summary>
    public async Task<IdentityAccount> SignInAsync(
        IdentityProvider provider,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(provider))
        {
            throw new AuthException(
                $"Esta compilación no lleva identificador de cliente de {provider}: hay que " +
                "rellenar oauth.local.props antes de poder entrar.");
        }

        var verifier = CreateVerifier();
        var redirect = RedirectUri(provider);

        var authorize = new Uri(
            $"{AuthorizeUrl(provider)}" +
            $"?client_id={ClientId(provider)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(redirect)}" +
            $"&scope={Uri.EscapeDataString(Scopes(provider))}" +
            $"&code_challenge={Challenge(verifier)}" +
            $"&code_challenge_method=S256" +
            ExtraAuthorizeParameters(provider));

        var callback = await _browser.AuthenticateAsync(authorize, cancellationToken).ConfigureAwait(false);
        var code = ReadParameter(callback, "code");

        if (string.IsNullOrEmpty(code))
        {
            var error = ReadParameter(callback, "error_description") ?? ReadParameter(callback, "error");
            throw new AuthException(error is null
                ? $"{provider} no devolvió ningún código de acceso."
                : $"{provider} rechazó la entrada: {error}");
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = ClientId(provider),
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirect,
            ["code_verifier"] = verifier,
        };

        AddSecret(provider, form);

        return await PostTokenAsync(provider, form, null, cancellationToken).ConfigureAwait(false)
            ?? throw new AuthException($"{provider} no aceptó el código de acceso.");
    }

    /// <summary>
    /// Renueva la identidad sin molestar al usuario. Devuelve <c>null</c> si el proveedor ya no
    /// acepta el token de refresco —cuenta revocada o permiso retirado—, que es la unica razon
    /// legitima para volver a pedir la entrada; los fallos de red se propagan, porque quedarse sin
    /// cobertura no puede echar a nadie de su propia lista.
    /// </summary>
    public Task<IdentityAccount?> RefreshAsync(
        IdentityProvider provider,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = ClientId(provider),
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = Scopes(provider),
        };

        AddSecret(provider, form);

        // Al renovar, algunos proveedores no reenvian el token de refresco: se conserva el anterior.
        return PostTokenAsync(provider, form, refreshToken, cancellationToken);
    }

    // -----------------------------------------------------------------------
    // Lo que cambia de un proveedor a otro
    // -----------------------------------------------------------------------

    private static string AuthorizeUrl(IdentityProvider provider) => provider switch
    {
        IdentityProvider.Google => "https://accounts.google.com/o/oauth2/v2/auth",
        _ => "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
    };

    private static string TokenUrl(IdentityProvider provider) => provider switch
    {
        IdentityProvider.Google => "https://oauth2.googleapis.com/token",
        _ => "https://login.microsoftonline.com/common/oauth2/v2.0/token",
    };

    /// <summary>
    /// Lo minimo para saber quien es. En Microsoft <c>offline_access</c> es lo que da el token de
    /// refresco; en Google eso se pide con <c>access_type=offline</c>.
    /// </summary>
    private static string Scopes(IdentityProvider provider) => provider switch
    {
        IdentityProvider.Google => "openid email profile",
        _ => "openid email profile offline_access",
    };

    private static string ExtraAuthorizeParameters(IdentityProvider provider) => provider switch
    {
        // Sin token de refresco habria que volver a abrir el navegador cada hora, y la entrada es
        // obligatoria: seria un peaje en cada arranque.
        IdentityProvider.Google => "&access_type=offline&prompt=select_account",
        _ => "&prompt=select_account",
    };

    /// <summary>
    /// <b>Es la redireccion la que decide el cliente de Google</b>, no el sistema operativo.
    /// </summary>
    /// <remarks>
    /// Google tiene dos tipos de cliente y cada uno se valida de una manera: el de <i>Android</i>
    /// comprueba el nombre de paquete y la huella SHA-1 de la firma, y el de <i>escritorio</i>
    /// comprueba que se vuelva a <c>127.0.0.1</c>. Preguntando por donde vuelve el navegador, la
    /// clase sirve igual en Windows y en Android sin saber en cual esta. Microsoft usa un unico
    /// cliente publico para todo.
    /// </remarks>
    private bool UsesLoopback => _browser.RedirectUri.StartsWith("http://127.0.0.1", StringComparison.Ordinal);

    private string ClientId(IdentityProvider provider) => provider switch
    {
        IdentityProvider.Microsoft => MailOAuthConfig.MicrosoftClientId,
        _ => UsesLoopback ? MailOAuthConfig.GoogleDesktopClientId : MailOAuthConfig.GoogleAndroidClientId,
    };

    /// <summary>Solo el cliente de escritorio de Google lo exige; los demas son publicos.</summary>
    private string ClientSecret(IdentityProvider provider) =>
        provider == IdentityProvider.Google && UsesLoopback
            ? MailOAuthConfig.GoogleDesktopClientSecret
            : string.Empty;

    private string RedirectUri(IdentityProvider provider) =>
        provider == IdentityProvider.Google && !UsesLoopback
            ? $"{MailOAuthConfig.GoogleAndroidRedirectScheme}:/oauth2redirect"
            : _browser.RedirectUri;

    private void AddSecret(IdentityProvider provider, Dictionary<string, string> form)
    {
        var secret = ClientSecret(provider);
        if (secret.Length > 0)
        {
            form["client_secret"] = secret;
        }
    }

    // -----------------------------------------------------------------------

    private async Task<IdentityAccount?> PostTokenAsync(
        IdentityProvider provider,
        Dictionary<string, string> form,
        string? fallbackRefresh,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(TokenUrl(provider), content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // invalid_grant es el "ya no vales": el usuario retiro el permiso o cambio la contraseña.
            if (body.Contains("invalid_grant", StringComparison.Ordinal))
            {
                return null;
            }

            throw new AuthException($"{provider} rechazó la entrada ({(int)response.StatusCode}): {body}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var idToken = root.TryGetProperty("id_token", out var id) ? id.GetString() : null;
        if (string.IsNullOrEmpty(idToken))
        {
            throw new AuthException($"{provider} no devolvió el id_token con la identidad de la cuenta.");
        }

        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;

        using var claims = ReadClaims(provider, idToken);
        var payload = claims.RootElement;

        var subject = SubjectOf(provider, payload);
        if (subject.Length == 0)
        {
            throw new AuthException($"El id_token de {provider} no traía el identificador de la cuenta.");
        }

        var email = Claim(payload, "email");
        if (email.Length == 0)
        {
            // Muchas cuentas de trabajo de Microsoft no publican `email`, pero si el usuario.
            email = Claim(payload, "preferred_username");
        }

        var name = Claim(payload, "name");
        if (name.Length == 0)
        {
            // Algunas cuentas de empresa no publican `name`, pero si el nombre y el apellido.
            name = $"{Claim(payload, "given_name")} {Claim(payload, "family_name")}".Trim();
        }

        // Si viene vacio, se deja vacio: al RENOVAR, el id_token no repite los datos del perfil, y
        // rellenarlo aqui con el correo pisaria el nombre de verdad con una direccion. Quien decide
        // que hacer con un hueco es quien tiene lo guardado (ver SupabaseAuthService).
        return new IdentityAccount(
            provider,
            subject,
            email,
            name,
            Claim(payload, "picture"),
            idToken,
            string.IsNullOrEmpty(refresh) ? fallbackRefresh : refresh,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    /// <summary>
    /// El identificador estable de la cuenta.
    /// </summary>
    /// <remarks>
    /// En Microsoft es <c>oid</c>, el identificador del usuario en el directorio, y no <c>sub</c>:
    /// <c>sub</c> es distinto para cada aplicacion, asi que serviria para reconocer al usuario pero
    /// no para nada mas, y cambiaria si algun dia se registrara otra aplicacion.
    /// </remarks>
    private static string SubjectOf(IdentityProvider provider, JsonElement payload)
    {
        if (provider == IdentityProvider.Microsoft)
        {
            var oid = Claim(payload, "oid");
            if (oid.Length > 0)
            {
                return oid;
            }
        }

        return Claim(payload, "sub");
    }

    /// <summary>
    /// Abre el id_token para leer sus datos. No se comprueba la firma <b>a proposito</b>: el token
    /// no viene del navegador sino de una respuesta directa del proveedor sobre TLS, asi que ya se
    /// sabe de quien es. La firma habria que comprobarla en el servidor, y de eso se encarga
    /// Supabase.
    /// </summary>
    private static JsonDocument ReadClaims(IdentityProvider provider, string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            throw new AuthException($"El id_token de {provider} no tiene el formato esperado.");
        }

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

        try
        {
            return JsonDocument.Parse(Convert.FromBase64String(payload));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new AuthException($"No se pudo leer la identidad que devolvió {provider}.");
        }
    }

    private static string Claim(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

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
