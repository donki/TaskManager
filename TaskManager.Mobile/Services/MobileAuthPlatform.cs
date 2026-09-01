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
    /// El esquema propio de la aplicacion, que coincide con el intent-filter de
    /// <c>WebAuthenticationCallbackActivity</c>. Es el que vale para Microsoft; Google impone el
    /// suyo y lo pone el propio servicio en la URL de autorizacion.
    /// </summary>
    public string RedirectUri => "com.socratic.taskmanager://auth";

    public async Task<Uri> AuthenticateAsync(Uri authorizeUrl, CancellationToken cancellationToken = default)
    {
        // La vuelta se saca de la propia URL de autorizacion, no de esta clase: Google exige en
        // Android el identificador de cliente invertido, y dar por hecho el esquema de la aplicacion
        // dejaria la pestana abierta esperando una vuelta que nunca coincide.
        var callbackUrl = new Uri(ReadParameter(authorizeUrl, "redirect_uri") ?? RedirectUri);

        var result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
        {
            Url = authorizeUrl,
            CallbackUrl = callbackUrl,
            PrefersEphemeralWebBrowserSession = false,
        });

        // WebAuthenticator ya ha troceado la respuesta; se rehace la URL para que el nucleo lea el
        // codigo igual que en el escritorio.
        var query = string.Join('&', result.Properties.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        return new Uri($"{callbackUrl}?{query}");
    }

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
