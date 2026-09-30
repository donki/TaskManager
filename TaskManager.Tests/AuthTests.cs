using System.Net;
using TaskManager.Core.Services;

namespace TaskManager.Tests;

public class AuthTests
{
    private const string GoogleToken = "https://oauth2.googleapis.com/token";
    private const string MicrosoftToken = "login.microsoftonline.com/common/oauth2/v2.0/token";
    private const string SupabaseIdToken = "/auth/v1/token?grant_type=id_token";
    private const string SupabaseRefresh = "/auth/v1/token?grant_type=refresh_token";
    internal const string RemoteId = "7a1c3d9e-1111-4222-8333-444455556666";

    internal sealed class Rig : IAsyncDisposable
    {
        public required TestStore Store { get; init; }
        public FakeHttp Http { get; } = new();
        public FakeTokens Tokens { get; } = new();
        public FakeBrowser Browser { get; init; } = new();
        public SupabaseAuthService Auth { get; private set; } = null!;

        public static async Task<Rig> CreateAsync(string account = "", FakeBrowser? browser = null)
        {
            var rig = new Rig { Store = await TestStore.CreateAsync(account), Browser = browser ?? new FakeBrowser() };
            rig.Auth = new SupabaseAuthService(rig.Http.Client(), rig.Store.Settings, rig.Tokens, rig.Browser);
            return rig;
        }

        public ValueTask DisposeAsync() => Store.DisposeAsync();
    }

    internal static object GoogleTokens(object claims, string? refresh = "refresco-google") => refresh is null
        ? new { id_token = Jwt.Make(claims), expires_in = 3600 }
        : new { id_token = Jwt.Make(claims), refresh_token = refresh, expires_in = 3600 };

    internal static object SupabaseSession(string access = "jwt-supabase", int expiresIn = 3600) => new
    {
        access_token = access,
        refresh_token = "refresco-supabase",
        expires_in = expiresIn,
        user = new { id = RemoteId },
    };

    /// <summary>Deja a alguien dentro con Google y con sesion del servidor, todo contra el servidor falso.</summary>
    internal static async Task<Rig> SignedInAsync()
    {
        var rig = await Rig.CreateAsync();
        if (!rig.Auth.IsConfiguredFor(IdentityProvider.Google))
        {
            // Sin oauth.local.props no hay cliente de Google: se entra con la sesion ya guardada.
            await rig.Store.Settings.SetAsync(SettingsService.KeyGoogleSub, "sub-123");
            await rig.Store.Settings.SetAsync(SettingsService.KeyUserId, "sub-123");
            await rig.Store.Settings.SetAsync(SettingsService.KeyRemoteUserId, RemoteId);
            await rig.Tokens.SetAsync("auth.access_token", "jwt-supabase");
            await rig.Tokens.SetAsync("auth.expires_at", DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
            return rig;
        }

        rig.Http.OnJson(HttpMethod.Post, GoogleToken, GoogleTokens(new { sub = "sub-123", email = "ana@example.com", name = "Ana", picture = "https://p/a.png" }));
        rig.Http.OnJson(HttpMethod.Post, SupabaseIdToken, SupabaseSession());
        await rig.Auth.SignInAsync(IdentityProvider.Google);
        return rig;
    }

    private static bool NoClient(Rig rig, IdentityProvider provider) => !rig.Auth.IsConfiguredFor(provider);

    [Fact]
    public async Task Configuration()
    {
        await using var rig = await Rig.CreateAsync();
        Assert.False(rig.Auth.IsConfiguredFor(IdentityProvider.Local));
        Assert.Equal(rig.Auth.Available.Count > 0, rig.Auth.IsConfigured);
        Assert.DoesNotContain(IdentityProvider.Local, rig.Auth.Available);
        Assert.False(rig.Auth.IsSignedIn);
        Assert.False(rig.Auth.IsLocalAccount);
    }

    [Fact]
    public async Task SignIn_Google_SetsIdentity_AndLinksSupabase()
    {
        await using var rig = await Rig.CreateAsync();
        if (NoClient(rig, IdentityProvider.Google))
        {
            await Assert.ThrowsAsync<AuthException>(() => rig.Auth.SignInAsync(IdentityProvider.Google));
            return;
        }

        var events = new List<AuthUser?>();
        rig.Auth.UserChanged += (_, u) => events.Add(u);
        rig.Http.OnJson(HttpMethod.Post, GoogleToken, GoogleTokens(new { sub = "sub-123", email = "ana@example.com", name = "Ana", picture = "https://p/a.png" }));
        rig.Http.OnJson(HttpMethod.Post, SupabaseIdToken, SupabaseSession());

        var user = await rig.Auth.SignInAsync(IdentityProvider.Google);

        Assert.Equal(new AuthUser("sub-123", "ana@example.com", "Ana", "https://p/a.png", RemoteId), user);
        Assert.True(rig.Auth.IsSignedIn);
        Assert.Equal("sub-123", rig.Store.Settings.Get(SettingsService.KeyGoogleSub));
        Assert.Equal("Google", rig.Store.Settings.Get(SettingsService.KeyAuthProvider));
        Assert.Equal(RemoteId, rig.Store.Settings.Get(SettingsService.KeyRemoteUserId));
        Assert.Equal("refresco-google", rig.Tokens.Values["auth.google_refresh"]);
        Assert.Equal("jwt-supabase", await rig.Auth.GetAccessTokenAsync());
        Assert.Null(rig.Auth.LastRemoteError);
        Assert.Equal(RemoteId, events[^1]!.RemoteId);

        // PKCE y cliente de escritorio: lo que va al navegador y lo que va al canje.
        var authorize = rig.Browser.LastAuthorize!.AbsoluteUri;
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", authorize);
        Assert.Contains("code_challenge_method=S256", authorize);
        Assert.Contains("access_type=offline", authorize);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1", authorize);
        var exchange = rig.Http.To(GoogleToken).Single().Body;
        Assert.Contains("grant_type=authorization_code", exchange);
        Assert.Contains("code=codigo-1", exchange);
        Assert.Contains("code_verifier=", exchange);
        Assert.Contains("client_secret=", exchange);
        Assert.Contains("\"provider\":\"google\"", rig.Http.To(SupabaseIdToken).Single().Body);
    }

    [Fact]
    public async Task SignIn_Microsoft_UsesOid_PreferredUsername_AndGivenNames_SupabaseDown()
    {
        await using var rig = await Rig.CreateAsync();
        if (NoClient(rig, IdentityProvider.Microsoft))
        {
            await Assert.ThrowsAsync<AuthException>(() => rig.Auth.SignInAsync(IdentityProvider.Microsoft));
            return;
        }

        rig.Http.OnJson(HttpMethod.Post, MicrosoftToken, GoogleTokens(new
        {
            sub = "sub-por-aplicacion",
            oid = "oid-estable",
            preferred_username = "ana@empresa.com",
            given_name = "Ana",
            family_name = "Pérez",
        }));
        rig.Http.On(HttpMethod.Post, SupabaseIdToken, HttpStatusCode.BadRequest, "provider not enabled");

        var user = await rig.Auth.SignInAsync(IdentityProvider.Microsoft);

        Assert.Equal("oid-estable", user.Id);
        Assert.Equal("ana@empresa.com", user.Email);
        Assert.Equal("Ana Pérez", user.DisplayName);
        Assert.Equal(string.Empty, user.RemoteId);
        Assert.Equal("provider not enabled", rig.Auth.LastRemoteError);
        Assert.Null(await rig.Auth.GetAccessTokenAsync());
        Assert.Contains("\"provider\":\"azure\"", rig.Http.To(SupabaseIdToken).Single().Body);
        Assert.DoesNotContain("client_secret", rig.Http.To(MicrosoftToken).Single().Body);
        Assert.Contains("offline_access", Uri.UnescapeDataString(rig.Browser.LastAuthorize!.ToString()));
    }

    [Fact]
    public async Task SignIn_NameFallsBackToEmail_AndSupabaseUnreachable()
    {
        await using var rig = await Rig.CreateAsync();
        if (NoClient(rig, IdentityProvider.Google))
        {
            return;
        }

        rig.Http.OnJson(HttpMethod.Post, GoogleToken, GoogleTokens(new { sub = "s", email = "solo@correo.es" }));
        rig.Http.Throw(HttpMethod.Post, SupabaseIdToken);

        var user = await rig.Auth.SignInAsync(IdentityProvider.Google);
        Assert.Equal("solo@correo.es", user.DisplayName);
        Assert.Equal("sin red", rig.Auth.LastRemoteError);
    }

    [Fact]
    public async Task SignIn_OnAndroid_GoogleReturnsByReversedScheme()
    {
        await using var rig = await Rig.CreateAsync(browser: new FakeBrowser("com.socratic.taskmanager://auth"));
        if (NoClient(rig, IdentityProvider.Google))
        {
            return;
        }

        rig.Http.OnJson(HttpMethod.Post, GoogleToken, GoogleTokens(new { sub = "s" }));
        rig.Browser.Callback = "com.socratic.taskmanager://auth?code=c";
        await rig.Auth.SignInAsync(IdentityProvider.Google);

        var redirect = Uri.UnescapeDataString(rig.Browser.LastAuthorize!.Query);
        Assert.Contains(":/oauth2redirect", redirect);
        Assert.DoesNotContain("127.0.0.1", redirect);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5123/auth/?error=access_denied", "access_denied")]
    [InlineData("http://127.0.0.1:5123/auth/?error=x&error_description=Cancelado%20por%20el%20usuario", "Cancelado por el usuario")]
    [InlineData("http://127.0.0.1:5123/auth/?state=solo", "ningún código")]
    public async Task SignIn_BrowserWithoutCode_Throws(string callback, string expected)
    {
        await using var rig = await Rig.CreateAsync();
        if (NoClient(rig, IdentityProvider.Google))
        {
            return;
        }

        rig.Browser.Callback = callback;
        var ex = await Assert.ThrowsAsync<AuthException>(() => rig.Auth.SignInAsync(IdentityProvider.Google));
        Assert.Contains(expected, ex.Message);
        Assert.False(rig.Auth.IsSignedIn);
    }

    public static TheoryData<HttpStatusCode, string, string> BadTokenResponses => new()
    {
        { HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}", "no aceptó" },
        { HttpStatusCode.InternalServerError, "caido", "rechazó la entrada (500)" },
        { HttpStatusCode.OK, "{\"access_token\":\"a\"}", "id_token" },
        { HttpStatusCode.OK, "{\"id_token\":\"sin-puntos\"}", "formato" },
        { HttpStatusCode.OK, "{\"id_token\":\"a.@@@.b\"}", "No se pudo leer" },
        { HttpStatusCode.OK, $"{{\"id_token\":\"{Jwt.Make(new { email = "x@y.z" })}\"}}", "identificador" },
    };

    [Theory]
    [MemberData(nameof(BadTokenResponses))]
    public async Task SignIn_BadTokenResponse_Throws(HttpStatusCode status, string body, string expected)
    {
        await using var rig = await Rig.CreateAsync();
        if (NoClient(rig, IdentityProvider.Google))
        {
            return;
        }

        rig.Http.On(HttpMethod.Post, GoogleToken, status, body);
        var ex = await Assert.ThrowsAsync<AuthException>(() => rig.Auth.SignInAsync(IdentityProvider.Google));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task SignIn_UnconfiguredProvider_Throws()
    {
        await using var rig = await Rig.CreateAsync();
        var ex = await Assert.ThrowsAsync<AuthException>(() => rig.Auth.SignInAsync(IdentityProvider.Local));
        Assert.Contains("oauth.local.props", ex.Message);
    }

    [Fact]
    public async Task SignInLocally_KeepsItsIdentifier_AndDropsRemoteSession()
    {
        await using var rig = await Rig.CreateAsync();
        await rig.Tokens.SetAsync("auth.access_token", "de otra cuenta");
        await rig.Store.Settings.SetAsync(SettingsService.KeyRemoteUserId, RemoteId);

        var first = await rig.Auth.SignInLocallyAsync();
        Assert.StartsWith("local-", first.Id);
        Assert.True(rig.Auth.IsLocalAccount);
        Assert.Null(await rig.Auth.GetAccessTokenAsync());
        Assert.Equal(string.Empty, rig.Store.Settings.Get(SettingsService.KeyRemoteUserId));

        await rig.Auth.SignOutAsync();
        Assert.False(rig.Auth.IsSignedIn);
        Assert.Equal(string.Empty, rig.Store.Settings.Get(SettingsService.KeyGoogleSub));

        Assert.Equal(first.Id, (await rig.Auth.SignInLocallyAsync()).Id);

        // Y al arrancar se recupera sin red.
        var again = new SupabaseAuthService(rig.Http.Client(), rig.Store.Settings, rig.Tokens, rig.Browser);
        Assert.Equal(first.Id, (await again.RestoreSessionAsync())!.Id);
        Assert.Empty(rig.Http.Calls);
    }

    [Fact]
    public async Task Restore_WithoutRefreshOrUser_IsNull()
    {
        await using var rig = await Rig.CreateAsync();
        Assert.Null(await rig.Auth.RestoreSessionAsync());

        await rig.Store.Settings.SetAsync(SettingsService.KeyAuthProvider, "Inventado");
        await rig.Tokens.SetAsync("auth.google_refresh", "r");
        Assert.Null(await rig.Auth.RestoreSessionAsync()); // sin usuario guardado
    }

    private static async Task SeedStoredGoogleUserAsync(Rig rig)
    {
        var s = rig.Store.Settings;
        await s.SetAsync(SettingsService.KeyGoogleSub, "sub-123");
        await s.SetAsync(SettingsService.KeyAuthProvider, "Google");
        await s.SetAsync(SettingsService.KeyDisplayName, "Josep Solà");
        await s.SetAsync(SettingsService.KeyAccountEmail, "josep@example.com");
        await rig.Tokens.SetAsync("auth.google_refresh", "refresco-google");
    }

    [Fact]
    public async Task Restore_RefreshKeepsStoredProfile()
    {
        await using var rig = await Rig.CreateAsync();
        await SeedStoredGoogleUserAsync(rig);
        // Al renovar, el id_token no repite el perfil.
        rig.Http.OnJson(HttpMethod.Post, GoogleToken, GoogleTokens(new { sub = "sub-123" }, refresh: null));
        rig.Http.OnJson(HttpMethod.Post, SupabaseIdToken, SupabaseSession());

        var user = await rig.Auth.RestoreSessionAsync();

        Assert.Equal("Josep Solà", user!.DisplayName);
        Assert.Equal("josep@example.com", user.Email);
        if (!NoClient(rig, IdentityProvider.Google))
        {
            Assert.Equal(RemoteId, user.RemoteId);
            Assert.Contains("grant_type=refresh_token", rig.Http.To(GoogleToken).Single().Body);
        }
    }

    [Fact]
    public async Task Restore_RevokedPermission_SignsOut()
    {
        await using var rig = await Rig.CreateAsync();
        await SeedStoredGoogleUserAsync(rig);
        rig.Http.On(HttpMethod.Post, GoogleToken, HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");

        var user = await rig.Auth.RestoreSessionAsync();
        if (NoClient(rig, IdentityProvider.Google))
        {
            return;
        }

        Assert.Null(user);
        Assert.False(rig.Auth.IsSignedIn);
        Assert.Null(rig.Tokens.Values["auth.google_refresh"]);
    }

    [Fact]
    public async Task Restore_WithoutNetwork_KeepsTheStoredUser()
    {
        await using var rig = await Rig.CreateAsync();
        await SeedStoredGoogleUserAsync(rig);
        rig.Http.Throw(HttpMethod.Post, GoogleToken);

        var user = await rig.Auth.RestoreSessionAsync();
        Assert.Equal("sub-123", user!.Id);
        Assert.True(rig.Auth.IsSignedIn);
    }

    [Fact]
    public async Task AccessToken_RefreshesWhenAboutToExpire()
    {
        await using var rig = await Rig.CreateAsync();
        Assert.Null(await rig.Auth.GetAccessTokenAsync());

        await rig.Tokens.SetAsync("auth.access_token", "viejo");
        await rig.Tokens.SetAsync("auth.expires_at", DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));

        // Sin refresco guardado: se devuelve el que hay y que lo decida el servidor.
        Assert.Equal("viejo", await rig.Auth.GetAccessTokenAsync());

        await rig.Tokens.SetAsync("auth.refresh_token", "r");
        rig.Http.On(HttpMethod.Post, SupabaseRefresh, HttpStatusCode.Unauthorized, "caducado");
        Assert.Equal("viejo", await rig.Auth.GetAccessTokenAsync());

        rig.Http.OnJson(HttpMethod.Post, SupabaseRefresh, new { access_token = "nuevo" });
        Assert.Equal("nuevo", await rig.Auth.GetAccessTokenAsync());
        Assert.Null(rig.Tokens.Values["auth.refresh_token"]);
        Assert.True(DateTimeOffset.Parse(rig.Tokens.Values["auth.expires_at"]!) > DateTimeOffset.UtcNow.AddMinutes(50));

        // Y mientras vale, ni se pregunta.
        var calls = rig.Http.Calls.Count;
        Assert.Equal("nuevo", await rig.Auth.GetAccessTokenAsync());
        Assert.Equal(calls, rig.Http.Calls.Count);
    }

    [Fact]
    public async Task AccessToken_GarbageExpiryCountsAsExpired()
    {
        await using var rig = await Rig.CreateAsync();
        await rig.Tokens.SetAsync("auth.access_token", "a");
        await rig.Tokens.SetAsync("auth.expires_at", "no es una fecha");
        await rig.Tokens.SetAsync("auth.refresh_token", "r");
        rig.Http.OnJson(HttpMethod.Post, SupabaseRefresh, SupabaseSession("b"));

        Assert.Equal("b", await rig.Auth.GetAccessTokenAsync());
        // Sin usuario dentro, el uid se guarda igual para cuando lo haya.
        Assert.Equal(RemoteId, rig.Store.Settings.Get(SettingsService.KeyRemoteUserId));
    }
}

public class MailOAuthTests
{
    private static (MailOAuthService Service, FakeHttp Http, FakeTokens Tokens, FakeBrowser Browser) Create()
    {
        var http = new FakeHttp();
        var tokens = new FakeTokens();
        var browser = new FakeBrowser();
        return (new MailOAuthService(http.Client(), browser, tokens), http, tokens, browser);
    }

    [Fact]
    public void AdminConsentUrl_OnlyForMicrosoft()
    {
        var (service, _, _, _) = Create();
        var url = service.BuildAdminConsentUrl(MailOAuthProvider.Microsoft, "contoso.com");
        Assert.StartsWith("https://login.microsoftonline.com/contoso.com/v2.0/adminconsent?client_id=", url);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1", url);
        Assert.Throws<NotSupportedException>(() => service.BuildAdminConsentUrl(MailOAuthProvider.Google));
    }

    [Fact]
    public void Config_And_Session()
    {
        Assert.Equal(MailOAuthConfig.ClientIdFor(MailOAuthProvider.Google).Length > 0, MailOAuthConfig.IsConfigured(MailOAuthProvider.Google));
        Assert.Equal(MailOAuthConfig.GoogleDesktopClientSecret, MailOAuthConfig.ClientSecretFor(MailOAuthProvider.Google));
        Assert.Equal(string.Empty, MailOAuthConfig.ClientSecretFor(MailOAuthProvider.Microsoft));
        Assert.NotNull(MailOAuthConfig.GoogleAndroidClientId);
        Assert.NotNull(MailOAuthConfig.GoogleAndroidRedirectScheme);
        Assert.NotNull(MailOAuthConfig.GoogleDesktopRedirectScheme);

        Assert.True(new MailOAuthSession("a", null, DateTimeOffset.UtcNow.AddMinutes(1)).IsExpired);
        Assert.False(new MailOAuthSession("a", null, DateTimeOffset.UtcNow.AddHours(1)).IsExpired);
    }

    [Theory]
    [InlineData("{\"error_codes\":[700016],\"error_description\":\"AADSTS700016: Application not found.\"}", "AppNotRegistered")]
    [InlineData("{\"error_description\":\"AADSTS50020: User account does not exist. Trace\"}", "AADSTS50020: User account does not exist")]
    [InlineData("{\"error_description\":\"AADSTS\"}", "AADSTS")]
    [InlineData("{\"device_code\":\"x\"}", null)]
    public async Task Preflight_Microsoft(string body, string? expected)
    {
        var (service, http, _, _) = Create();
        if (!MailOAuthConfig.IsConfigured(MailOAuthProvider.Microsoft))
        {
            Assert.Equal("NoClientId", await service.PreflightAsync(MailOAuthProvider.Microsoft));
            return;
        }

        http.On(HttpMethod.Post, "/devicecode", HttpStatusCode.BadRequest, body);
        Assert.Equal(expected, await service.PreflightAsync(MailOAuthProvider.Microsoft));
    }

    [Fact]
    public async Task Preflight_GoogleAndNetworkErrors_AreNotBlocking()
    {
        var (service, http, _, _) = Create();
        if (!MailOAuthConfig.IsConfigured(MailOAuthProvider.Microsoft))
        {
            return;
        }

        Assert.Null(await service.PreflightAsync(MailOAuthProvider.Google));
        http.Throw(HttpMethod.Post, "/devicecode");
        Assert.Null(await service.PreflightAsync(MailOAuthProvider.Microsoft));
    }

    [Fact]
    public async Task SignIn_Restore_Refresh_SignOut()
    {
        var (service, http, tokens, browser) = Create();
        if (!MailOAuthConfig.IsConfigured(MailOAuthProvider.Google))
        {
            await Assert.ThrowsAsync<MailException>(() => service.SignInAsync(MailOAuthProvider.Google));
            return;
        }

        Assert.Null(await service.RestoreAsync(MailOAuthProvider.Google));

        http.OnJson(HttpMethod.Post, MailOAuthProvider.Google.TokenUrl, new { access_token = "acc", refresh_token = "ref", expires_in = 60 });
        var session = await service.SignInAsync(MailOAuthProvider.Google);
        Assert.Equal("acc", session.AccessToken);
        Assert.Contains("prompt=consent", browser.LastAuthorize!.ToString());
        Assert.Contains("client_secret=", http.Calls[^1].Body);
        Assert.Equal("ref", tokens.Values["mail.google.refresh"]);

        // Caduca en un minuto: al restaurar se renueva, y el refresco se conserva si no viene otro.
        http.OnJson(HttpMethod.Post, MailOAuthProvider.Google.TokenUrl, new { access_token = "acc2" });
        var renewed = await service.RestoreAsync(MailOAuthProvider.Google);
        Assert.Equal("acc2", renewed!.AccessToken);
        Assert.Equal("ref", renewed.RefreshToken);
        Assert.Contains("grant_type=refresh_token", http.Calls[^1].Body);

        // Vigente: no se pregunta.
        var calls = http.Calls.Count;
        Assert.Equal("acc2", (await service.RestoreAsync(MailOAuthProvider.Google))!.AccessToken);
        Assert.Equal(calls, http.Calls.Count);

        await service.SignOutAsync(MailOAuthProvider.Google);
        Assert.Null(await service.RestoreAsync(MailOAuthProvider.Google));
    }

    [Fact]
    public async Task Restore_ExpiredWithoutRefresh_ReturnsWhatThereIs()
    {
        var (service, _, tokens, _) = Create();
        await tokens.SetAsync("mail.microsoft.access", "a");
        var session = await service.RestoreAsync(MailOAuthProvider.Microsoft);
        Assert.Equal("a", session!.AccessToken);
        Assert.True(session.IsExpired);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5123/auth/?error=access_denied", HttpStatusCode.OK, "{}", "access_denied")]
    [InlineData("http://127.0.0.1:5123/auth/", HttpStatusCode.OK, "{}", "ningún código")]
    [InlineData("http://127.0.0.1:5123/auth/?code=c", HttpStatusCode.BadRequest, "malo", "rechazó la sesión (400)")]
    [InlineData("http://127.0.0.1:5123/auth/?code=c", HttpStatusCode.OK, "{\"refresh_token\":\"r\"}", "ningún token")]
    public async Task SignIn_Errors(string callback, HttpStatusCode status, string body, string expected)
    {
        var (service, http, _, browser) = Create();
        if (!MailOAuthConfig.IsConfigured(MailOAuthProvider.Microsoft))
        {
            return;
        }

        browser.Callback = callback;
        http.On(HttpMethod.Post, MailOAuthProvider.Microsoft.TokenUrl, status, body);
        var ex = await Assert.ThrowsAsync<MailException>(() => service.SignInAsync(MailOAuthProvider.Microsoft));
        Assert.Contains(expected, ex.Message);
    }
}

public class MailKitReaderTests
{
    [Fact]
    public async Task IncompleteAccount_Throws()
    {
        var ex = await Assert.ThrowsAsync<MailException>(() =>
            new MailKitReader().FetchAsync(new MailAccount("x", "sin-arroba", "", 993, "", 587), "s"));
        Assert.Contains("Faltan datos", ex.Message);
    }

    /// <summary>Un puerto cerrado en el propio equipo: el fallo de conexion sale como MailException.</summary>
    [Fact]
    public async Task UnreachableServer_IsAMailException()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var account = new MailAccount("Otro", "a@b.c", "127.0.0.1", port, "", 587);
        var ex = await Assert.ThrowsAsync<MailException>(() => new MailKitReader().FetchAsync(account, "s", useOAuth: true));
        Assert.StartsWith("No se ha podido leer el buzón", ex.Message);
        Assert.NotNull(ex.InnerException);
    }
}
