using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TaskManager.Core.Services;

/// <summary>Usuario autenticado. El <see cref="Id"/> es el <c>auth.uid()</c> de Supabase.</summary>
public sealed record AuthUser(string Id, string Email, string DisplayName, string AvatarUrl);

/// <summary>
/// Donde se guardan los tokens. Cada plataforma pone el suyo: en Android el almacen seguro del
/// sistema, en Windows DPAPI. La base de datos en claro es el ultimo recurso.
/// </summary>
public interface ITokenStore
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string? value);
}

/// <summary>Abre el navegador del sistema y devuelve la URL de vuelta con el codigo.</summary>
public interface IOAuthBrowser
{
    /// <summary>A donde vuelve Google/Supabase. Tiene que estar dado de alta en el proyecto.</summary>
    string RedirectUri { get; }

    Task<Uri> AuthenticateAsync(Uri authorizeUrl, CancellationToken cancellationToken = default);
}

public sealed class AuthException(string message) : Exception(message);

/// <summary>
/// Entrada con Google a traves de Supabase Auth (especificacion 2.C). El usuario queda guardado en
/// <c>auth.users</c> y su perfil en <c>profiles</c>, que es lo que ven sus companeros de grupo.
///
/// Se usa **PKCE**: el codigo vuelve como parametro de consulta, asi que sirve igual el esquema
/// propio de Android que el localhost del escritorio. El flujo implicito no valdria en Windows,
/// porque el token viaja en el fragmento y el fragmento no llega nunca al servidor local.
/// </summary>
public sealed class SupabaseAuthService
{
    private const string KeyAccessToken = "auth.access_token";
    private const string KeyRefreshToken = "auth.refresh_token";
    private const string KeyExpiresAt = "auth.expires_at";
    private const string KeyVerifier = "auth.pkce_verifier";

    private readonly HttpClient _http;
    private readonly SettingsService _settings;
    private readonly ITokenStore _tokens;
    private readonly IOAuthBrowser _browser;

    public SupabaseAuthService(HttpClient http, SettingsService settings, ITokenStore tokens, IOAuthBrowser browser)
    {
        _http = http;
        _settings = settings;
        _tokens = tokens;
        _browser = browser;
    }

    /// <summary>Usuario de la sesion actual, o null si no ha entrado nadie.</summary>
    public AuthUser? CurrentUser { get; private set; }

    public bool IsSignedIn => CurrentUser is not null;

    /// <summary>Sin proyecto de Supabase no hay con quien autenticarse.</summary>
    public bool IsConfigured => _settings.IsSupabaseConfigured;

    public event EventHandler<AuthUser?>? UserChanged;

    // -----------------------------------------------------------------------
    // Sesion
    // -----------------------------------------------------------------------

    /// <summary>
    /// Recupera la sesion guardada al arrancar. Si el token caduco se renueva con el de refresco,
    /// de modo que solo se pide entrar otra vez cuando de verdad hace falta.
    /// </summary>
    public async Task<AuthUser?> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var access = await _tokens.GetAsync(KeyAccessToken).ConfigureAwait(false);
        var refresh = await _tokens.GetAsync(KeyRefreshToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(access) && string.IsNullOrEmpty(refresh))
        {
            return null;
        }

        var expiresAt = DateTimeOffset.TryParse(await _tokens.GetAsync(KeyExpiresAt).ConfigureAwait(false), out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

        try
        {
            if (expiresAt <= DateTimeOffset.UtcNow.AddMinutes(2) && !string.IsNullOrEmpty(refresh))
            {
                await RefreshAsync(refresh, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await LoadUserAsync(access!, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Una sesion que ya no vale no puede impedir usar la aplicacion: se limpia y a seguir.
            await SignOutAsync().ConfigureAwait(false);
            return null;
        }

        return CurrentUser;
    }

    /// <summary>Token valido para llamar a la API, renovandolo si toca. Null si no hay sesion.</summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var access = await _tokens.GetAsync(KeyAccessToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(access))
        {
            return null;
        }

        var expiresAt = DateTimeOffset.TryParse(await _tokens.GetAsync(KeyExpiresAt).ConfigureAwait(false), out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

        if (expiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return access;
        }

        var refresh = await _tokens.GetAsync(KeyRefreshToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(refresh))
        {
            return access;
        }

        await RefreshAsync(refresh, cancellationToken).ConfigureAwait(false);
        return await _tokens.GetAsync(KeyAccessToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Entrar con Google
    // -----------------------------------------------------------------------

    public async Task<AuthUser> SignInWithGoogleAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new AuthException("La aplicación no tiene servidor configurado.");
        }

        var verifier = CreateVerifier();
        await _tokens.SetAsync(KeyVerifier, verifier).ConfigureAwait(false);

        var authorize = new Uri(
            $"{BaseUrl}/auth/v1/authorize" +
            $"?provider=google" +
            $"&redirect_to={Uri.EscapeDataString(_browser.RedirectUri)}" +
            $"&code_challenge={Challenge(verifier)}" +
            $"&code_challenge_method=s256");

        var callback = await _browser.AuthenticateAsync(authorize, cancellationToken).ConfigureAwait(false);
        var code = ReadParameter(callback, "code");

        if (string.IsNullOrEmpty(code))
        {
            var error = ReadParameter(callback, "error_description") ?? ReadParameter(callback, "error");
            throw new AuthException(error is null
                ? "Google no devolvió ningún código de acceso."
                : $"Google rechazó la entrada: {error}");
        }

        await ExchangeAsync(code, verifier, cancellationToken).ConfigureAwait(false);
        return CurrentUser ?? throw new AuthException("La sesión no trajo ningún usuario.");
    }

    /// <summary>
    /// Canjea el identificador de instalacion por una sesion anonima de Supabase. Es lo que permite
    /// no tener pantalla de entrada y aun asi mandar un JWT de verdad, que es lo unico con lo que la
    /// RLS sabe quien pregunta (ver <see cref="AuthOptions"/>).
    /// </summary>
    public async Task<AuthUser?> SignInAnonymouslyAsync(string installationId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || !AuthOptions.AnonymousSessionEnabled)
        {
            return null;
        }

        // El identificador de instalacion viaja como metadato: sirve para reconocer el dispositivo
        // en el servidor, no para autorizar nada (de eso se encarga el JWT).
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/auth/v1/signup")
        {
            Content = JsonContent(new { data = new { installation_id = installationId } }),
        };
        request.Headers.Add("apikey", AnonKey);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await StoreSessionAsync(response, cancellationToken).ConfigureAwait(false);
        return CurrentUser;
    }

    public async Task SignOutAsync()
    {
        await _tokens.SetAsync(KeyAccessToken, null).ConfigureAwait(false);
        await _tokens.SetAsync(KeyRefreshToken, null).ConfigureAwait(false);
        await _tokens.SetAsync(KeyExpiresAt, null).ConfigureAwait(false);

        CurrentUser = null;
        await _settings.SetAsync(SettingsService.KeyAccountEmail, string.Empty).ConfigureAwait(false);
        await _settings.SetAsync(SettingsService.KeyAvatarUrl, string.Empty).ConfigureAwait(false);
        UserChanged?.Invoke(this, null);
    }

    // -----------------------------------------------------------------------

    private static string BaseUrl => SupabaseConfig.Url.TrimEnd('/');

    private static string AnonKey => SupabaseConfig.PublishableKey;

    private async Task ExchangeAsync(string code, string verifier, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/auth/v1/token?grant_type=pkce")
        {
            Content = JsonContent(new { auth_code = code, code_verifier = verifier }),
        };
        request.Headers.Add("apikey", AnonKey);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await StoreSessionAsync(response, cancellationToken).ConfigureAwait(false);
        await _tokens.SetAsync(KeyVerifier, null).ConfigureAwait(false);
    }

    private async Task RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/auth/v1/token?grant_type=refresh_token")
        {
            Content = JsonContent(new { refresh_token = refreshToken }),
        };
        request.Headers.Add("apikey", AnonKey);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await StoreSessionAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task StoreSessionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AuthException($"Supabase rechazó la sesión ({(int)response.StatusCode}): {body}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var access = root.GetProperty("access_token").GetString()
            ?? throw new AuthException("La respuesta no traía access_token.");
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;

        await _tokens.SetAsync(KeyAccessToken, access).ConfigureAwait(false);
        await _tokens.SetAsync(KeyRefreshToken, refresh).ConfigureAwait(false);
        await _tokens.SetAsync(KeyExpiresAt, DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToString("O")).ConfigureAwait(false);

        // La propia respuesta trae el usuario; si no, se pide aparte.
        if (root.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
        {
            await ApplyUserAsync(user).ConfigureAwait(false);
        }
        else
        {
            await LoadUserAsync(access, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task LoadUserAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/auth/v1/user");
        request.Headers.Add("apikey", AnonKey);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AuthException($"No se pudo leer el usuario ({(int)response.StatusCode}).");
        }

        using var document = JsonDocument.Parse(body);
        await ApplyUserAsync(document.RootElement).ConfigureAwait(false);
    }

    /// <summary>
    /// Guarda la identidad. El id de Supabase pasa a ser el del usuario en la base local, para que
    /// el XP y la autoria de las tareas se atribuyan a la cuenta y no al identificador provisional.
    /// </summary>
    private async Task ApplyUserAsync(JsonElement user)
    {
        var id = user.GetProperty("id").GetString() ?? string.Empty;
        var email = user.TryGetProperty("email", out var e) ? e.GetString() ?? string.Empty : string.Empty;

        var name = email;
        var avatar = string.Empty;
        if (user.TryGetProperty("user_metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
        {
            if (metadata.TryGetProperty("full_name", out var fullName) && fullName.GetString() is { Length: > 0 } n)
            {
                name = n;
            }
            else if (metadata.TryGetProperty("name", out var shortName) && shortName.GetString() is { Length: > 0 } n2)
            {
                name = n2;
            }

            if (metadata.TryGetProperty("avatar_url", out var picture))
            {
                avatar = picture.GetString() ?? string.Empty;
            }
        }

        CurrentUser = new AuthUser(id, email, name, avatar);

        await _settings.SetAsync(SettingsService.KeyUserId, id).ConfigureAwait(false);
        await _settings.SetAsync(SettingsService.KeyAccountEmail, email).ConfigureAwait(false);
        await _settings.SetAsync(SettingsService.KeyAvatarUrl, avatar).ConfigureAwait(false);

        // El nombre visible solo se rellena si el usuario no ha puesto uno propio.
        if (_settings.Get(SettingsService.KeyDisplayName).Length == 0)
        {
            await _settings.SetAsync(SettingsService.KeyDisplayName, name).ConfigureAwait(false);
        }

        UserChanged?.Invoke(this, CurrentUser);
    }

    private static HttpContent JsonContent(object payload) =>
        new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static string? ReadParameter(Uri uri, string name)
    {
        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == name)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private static string CreateVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Base64Url(bytes);
    }

    private static string Challenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
