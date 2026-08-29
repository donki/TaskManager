using Microsoft.Maui.Authentication;
using Microsoft.Maui.Storage;
using TaskManager.Core.Services;

namespace TaskManager.Mobile.Services;

/// <summary>
/// Navegador del sistema para la entrada con Google. <see cref="WebAuthenticator"/> abre una
/// pestana de Chrome Custom Tabs —no un WebView incrustado, que Google rechaza desde 2021— y
/// devuelve el control por el esquema propio de la aplicacion.
/// </summary>
public sealed class MauiOAuthBrowser : IOAuthBrowser
{
    /// <summary>
    /// Tiene que estar dado de alta en Supabase (Authentication › URL Configuration › Redirect URLs)
    /// y coincidir con el intent-filter de <c>WebAuthenticationCallbackActivity</c>.
    /// </summary>
    public string RedirectUri => "com.socratic.taskmanager://auth";

    public async Task<Uri> AuthenticateAsync(Uri authorizeUrl, CancellationToken cancellationToken = default)
    {
        var result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
        {
            Url = authorizeUrl,
            CallbackUrl = new Uri(RedirectUri),
            PrefersEphemeralWebBrowserSession = false,
        });

        // WebAuthenticator ya ha troceado la respuesta; se rehace la URL para que el nucleo lea el
        // codigo igual que en el escritorio.
        var query = string.Join('&', result.Properties.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        return new Uri($"{RedirectUri}?{query}");
    }
}

/// <summary>
/// Tokens en el almacen seguro de Android (respaldado por el Keystore). Si el dispositivo lo tiene
/// roto —pasa en algunos fabricantes tras restaurar copia—, se cae a la tabla de ajustes para no
/// dejar al usuario sin poder entrar.
/// </summary>
public sealed class SecureTokenStore : ITokenStore
{
    private readonly ITokenStore _fallback;

    public SecureTokenStore(ITokenStore fallback) => _fallback = fallback;

    public async Task<string?> GetAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch (Exception)
        {
            return await _fallback.GetAsync(key);
        }
    }

    public async Task SetAsync(string key, string? value)
    {
        try
        {
            if (string.IsNullOrEmpty(value))
            {
                SecureStorage.Default.Remove(key);
            }
            else
            {
                await SecureStorage.Default.SetAsync(key, value);
            }
        }
        catch (Exception)
        {
            await _fallback.SetAsync(key, value);
        }
    }
}
