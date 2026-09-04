using System.Text;
using System.Text.Json;

namespace TaskManager.Core.Services;

/// <summary>
/// Usuario autenticado.
/// </summary>
/// <param name="Id">
/// El identificador de la cuenta que da el proveedor —el <c>sub</c> de Google o el <c>oid</c> de
/// Microsoft—, igual en todos los dispositivos. Es lo que la aplicacion usa como usuario en su base
/// local (autoria, XP, rachas).
/// </param>
/// <param name="RemoteId">
/// El <c>auth.uid()</c> de Supabase, cuando hay sesion en el servidor. Es distinto del
/// <paramref name="Id"/> y solo sirve para las filas que suben: la RLS esta escrita contra el.
/// Vacio mientras el proyecto no tenga dado de alta ese proveedor.
/// </param>
public sealed record AuthUser(string Id, string Email, string DisplayName, string AvatarUrl, string RemoteId = "");

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
    /// <summary>A donde vuelve el proveedor. Tiene que estar dado de alta en el cliente OAuth.</summary>
    string RedirectUri { get; }

    Task<Uri> AuthenticateAsync(Uri authorizeUrl, CancellationToken cancellationToken = default);
}

public sealed class AuthException(string message) : Exception(message);

/// <summary>
/// La entrada de la aplicacion: <b>identidad del proveedor</b> —Google o Microsoft— y, encima, la
/// sesion de Supabase que hace falta para sincronizar.
/// </summary>
/// <remarks>
/// <para><b>Quien eres lo dice el proveedor.</b> Se habla con el directamente
/// (<see cref="IdentitySignInService"/>) y de ahi salen el identificador de la cuenta y el nombre,
/// que es lo que la aplicacion usa como usuario y como nombre visible. Antes se pasaba por
/// <c>/auth/v1/authorize</c> de Supabase, y con el proveedor sin dar de alta en el proyecto el
/// navegador se quedaba en una pagina de Supabase con un error: la entrada no puede depender de un
/// ajuste del servidor.</para>
///
/// <para><b>La sesion de Supabase es aparte, y opcional.</b> El id_token que firma el proveedor se
/// canjea por un JWT del proyecto (<c>grant_type=id_token</c>), que es lo unico que la RLS entiende.
/// Si el proyecto todavia no tiene ese proveedor dado de alta, el canje falla y no pasa nada: se
/// entra igual y la aplicacion funciona en local. Cuando se active, la sincronizacion empieza a
/// funcionar sola sin tocar el cliente.</para>
///
/// <para><b>Al arrancar no se pide red.</b> La identidad guardada vale desde el primer momento y la
/// renovacion va detras: un equipo sin conexion no puede dejar al usuario fuera de sus propias
/// tareas. Solo se cierra la sesion cuando el proveedor dice que el permiso ya no vale.</para>
/// </remarks>
public sealed class SupabaseAuthService
{
    /// <summary>
    /// El token de refresco del proveedor. El nombre guardado sigue siendo <c>auth.google_refresh</c>
    /// aunque ahora valga tambien para Microsoft: cambiarlo dejaria sin sesion a quien actualizara,
    /// porque la aplicacion buscaria una clave que en su almacen no existe.
    /// </summary>
    private const string KeyRefresh = "auth.google_refresh";
    private const string KeyAccessToken = "auth.access_token";
    private const string KeyRefreshToken = "auth.refresh_token";
    private const string KeyExpiresAt = "auth.expires_at";

    private readonly HttpClient _http;
    private readonly SettingsService _settings;
    private readonly ITokenStore _tokens;
    private readonly IdentitySignInService _identity;

    public SupabaseAuthService(HttpClient http, SettingsService settings, ITokenStore tokens, IOAuthBrowser browser)
    {
        _http = http;
        _settings = settings;
        _tokens = tokens;
        _identity = new IdentitySignInService(http, browser);
    }

    /// <summary>Usuario de la sesion actual, o null si no ha entrado nadie.</summary>
    public AuthUser? CurrentUser { get; private set; }

    public bool IsSignedIn => CurrentUser is not null;

    /// <summary>Sin ningun cliente OAuth no hay a quien pedirle la entrada.</summary>
    public bool IsConfigured => Available.Count > 0;

    /// <summary>Con que cuentas se puede entrar en esta compilacion.</summary>
    public IReadOnlyList<IdentityProvider> Available => _identity.Available;

    public bool IsConfiguredFor(IdentityProvider provider) => _identity.IsConfigured(provider);

    public event EventHandler<AuthUser?>? UserChanged;

    // -----------------------------------------------------------------------
    // Sesion
    // -----------------------------------------------------------------------

    /// <summary>
    /// Recupera la sesion guardada al arrancar. Devuelve el usuario en cuanto lo tiene de la base
    /// local, sin esperar a la red; la renovacion contra el proveedor va despues y solo cierra la
    /// sesion si el permiso ha dejado de valer.
    /// </summary>
    public async Task<AuthUser?> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var refresh = await _tokens.GetAsync(KeyRefresh).ConfigureAwait(false);
        var userId = _settings.Get(SettingsService.KeyGoogleSub);

        if (string.IsNullOrEmpty(refresh) || userId.Length == 0)
        {
            return null;
        }

        var provider = Enum.TryParse<IdentityProvider>(
            _settings.Get(SettingsService.KeyAuthProvider, nameof(IdentityProvider.Google)), out var parsed)
            ? parsed
            : IdentityProvider.Google;

        CurrentUser = new AuthUser(
            userId,
            _settings.AccountEmail,
            _settings.DisplayName,
            _settings.AvatarUrl,
            _settings.Get(SettingsService.KeyRemoteUserId));

        UserChanged?.Invoke(this, CurrentUser);

        try
        {
            var account = await _identity.RefreshAsync(provider, refresh, cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                // El proveedor ya no reconoce el permiso: hay que volver a entrar de verdad.
                await SignOutAsync().ConfigureAwait(false);
                return null;
            }

            await ApplyAccountAsync(account).ConfigureAwait(false);
            await TryLinkSupabaseAsync(account, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Sin red se sigue con lo guardado: la aplicacion es local antes que nada.
        }

        return CurrentUser;
    }

    /// <summary>Token de Supabase valido para llamar a la API. Null si el proyecto aun no lo da.</summary>
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

        try
        {
            await RefreshSupabaseAsync(refresh, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return access;
        }

        return await _tokens.GetAsync(KeyAccessToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Entrar (y cambiar de cuenta)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Abre el navegador, entra con el proveedor elegido y deja la identidad puesta. El canje por la
    /// sesion de Supabase va detras y no puede tumbar la entrada: se entra aunque el servidor no
    /// responda.
    /// </summary>
    /// <remarks>
    /// Sirve igual para <b>entrar</b> y para <b>cambiar de cuenta</b>: entrar con la otra es
    /// exactamente esto, y lo de la anterior no se toca —cada cuenta tiene sus listas en el mismo
    /// aparato (<see cref="Models.TaskList.AccountId"/>)—, asi que volver a ella es entrar otra vez
    /// y encontrarlo todo donde estaba.
    /// </remarks>
    public async Task<AuthUser> SignInAsync(
        IdentityProvider provider,
        CancellationToken cancellationToken = default)
    {
        var account = await _identity.SignInAsync(provider, cancellationToken).ConfigureAwait(false);

        // La sesion del servidor que hubiera es de la cuenta anterior y ya no vale: se tira ANTES
        // de poner la identidad nueva. Si no, un canje que fallara —el proveedor sin dar de alta en
        // el proyecto, o sin red— dejaria el token y el `auth.uid()` de la cuenta de antes junto al
        // usuario nuevo, y lo de este subiria a nombre de aquel.
        await ClearRemoteSessionAsync().ConfigureAwait(false);

        await _tokens.SetAsync(KeyRefresh, account.RefreshToken).ConfigureAwait(false);
        await ApplyAccountAsync(account).ConfigureAwait(false);
        await TryLinkSupabaseAsync(account, cancellationToken).ConfigureAwait(false);

        return CurrentUser ?? throw new AuthException("La entrada no trajo ningún usuario.");
    }

    public async Task SignOutAsync()
    {
        await _tokens.SetAsync(KeyRefresh, null).ConfigureAwait(false);
        await ClearRemoteSessionAsync().ConfigureAwait(false);

        CurrentUser = null;
        await _settings.SetAsync(SettingsService.KeyGoogleSub, string.Empty).ConfigureAwait(false);
        await _settings.SetAsync(SettingsService.KeyAuthProvider, string.Empty).ConfigureAwait(false);
        await _settings.SetAsync(SettingsService.KeyAccountEmail, string.Empty).ConfigureAwait(false);
        await _settings.SetAsync(SettingsService.KeyAvatarUrl, string.Empty).ConfigureAwait(false);
        UserChanged?.Invoke(this, null);
    }

    /// <summary>
    /// Tira la sesion del proyecto: los tokens y el <c>auth.uid()</c>, que es con lo que se firma y
    /// se cifra lo que sube. Lo local no se toca.
    /// </summary>
    private async Task ClearRemoteSessionAsync()
    {
        await _tokens.SetAsync(KeyAccessToken, null).ConfigureAwait(false);
        await _tokens.SetAsync(KeyRefreshToken, null).ConfigureAwait(false);
        await _tokens.SetAsync(KeyExpiresAt, null).ConfigureAwait(false);
        await _settings.SetAsync(SettingsService.KeyRemoteUserId, string.Empty).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------

    private static string BaseUrl => SupabaseConfig.Url.TrimEnd('/');

    private static string AnonKey => SupabaseConfig.PublishableKey;

    /// <summary>
    /// Guarda la identidad que acaba de dar el proveedor.
    /// </summary>
    /// <remarks>
    /// El nombre de la cuenta pasa a ser <b>el nombre en la aplicacion</b>, se sobreescriba lo que
    /// hubiera: con la entrada obligatoria ya no hay ningun "yo" provisional que respetar, y quien
    /// quiera otro nombre lo cambia en los ajustes despues.
    /// </remarks>
    private async Task ApplyAccountAsync(IdentityAccount account)
    {
        // Lo que el proveedor no diga, se conserva. Al renovar, el id_token trae la identidad pero
        // no repite el perfil: sin esto, cada arranque cambiaba el nombre «Josep Solà» por el
        // correo, que es lo unico que quedaba.
        var email = Keep(account.Email, _settings.AccountEmail);
        var name = Keep(account.Name, _settings.Get(SettingsService.KeyDisplayName));
        var avatar = Keep(account.Picture, _settings.AvatarUrl);

        // Y si nunca ha habido nombre, el correo es mejor que un hueco.
        if (name.Length == 0)
        {
            name = email;
        }

        CurrentUser = new AuthUser(
            account.UserId,
            email,
            name,
            avatar,
            _settings.Get(SettingsService.KeyRemoteUserId));

        await _settings.SetAsync(SettingsService.KeyGoogleSub, account.UserId).ConfigureAwait(false);
        await _settings.SetAsync(SettingsService.KeyUserId, account.UserId).ConfigureAwait(false);
        await _settings.SetAsync(SettingsService.KeyAuthProvider, account.Provider.ToString()).ConfigureAwait(false);
        await _settings.SetAsync(SettingsService.KeyAccountEmail, email).ConfigureAwait(false);
        await _settings.SetAsync(SettingsService.KeyAvatarUrl, avatar).ConfigureAwait(false);
        await _settings.SetAsync(SettingsService.KeyDisplayName, name).ConfigureAwait(false);

        UserChanged?.Invoke(this, CurrentUser);
    }

    /// <summary>
    /// Canjea el id_token del proveedor por una sesion del proyecto de Supabase, que es lo unico que
    /// la RLS entiende. <b>De cortesia</b>: si el proyecto no tiene el proveedor dado de alta responde
    /// un 400 y la aplicacion sigue funcionando en local, sin sincronizar.
    /// </summary>
    private async Task TryLinkSupabaseAsync(IdentityAccount account, CancellationToken cancellationToken)
    {
        if (!SupabaseConfig.IsConfigured)
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/auth/v1/token?grant_type=id_token")
            {
                Content = JsonContent(new { provider = SupabaseProviderOf(account.Provider), id_token = account.IdToken }),
            };
            request.Headers.Add("apikey", AnonKey);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LastRemoteError = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await StoreSupabaseSessionAsync(response, cancellationToken).ConfigureAwait(false);
            LastRemoteError = null;
        }
        catch (Exception ex)
        {
            LastRemoteError = ex.Message;
        }
    }

    /// <summary>Por que no hay sincronizacion, cuando no la hay. Solo para poder contarlo.</summary>
    public string? LastRemoteError { get; private set; }

    /// <summary>
    /// Como llama Supabase a cada proveedor. Microsoft es «azure» alli, que es el nombre que tenia
    /// Entra ID cuando se escribio esa parte del servidor.
    /// </summary>
    private static string SupabaseProviderOf(IdentityProvider provider) =>
        provider == IdentityProvider.Microsoft ? "azure" : "google";

    private async Task RefreshSupabaseAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/auth/v1/token?grant_type=refresh_token")
        {
            Content = JsonContent(new { refresh_token = refreshToken }),
        };
        request.Headers.Add("apikey", AnonKey);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await StoreSupabaseSessionAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task StoreSupabaseSessionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
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

        // El uid del proyecto no es el de Google: se guarda aparte porque es el que firma las filas.
        if (root.TryGetProperty("user", out var user)
            && user.ValueKind == JsonValueKind.Object
            && user.TryGetProperty("id", out var id)
            && id.GetString() is { Length: > 0 } remoteId)
        {
            await _settings.SetAsync(SettingsService.KeyRemoteUserId, remoteId).ConfigureAwait(false);

            if (CurrentUser is not null)
            {
                CurrentUser = CurrentUser with { RemoteId = remoteId };
                UserChanged?.Invoke(this, CurrentUser);
            }
        }
    }

    /// <summary>Lo nuevo si dice algo; si no, lo que ya habia.</summary>
    private static string Keep(string incoming, string stored) =>
        incoming.Length > 0 ? incoming : stored;

    private static HttpContent JsonContent(object payload) =>
        new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
