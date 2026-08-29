using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TaskManager.Core.Services;

namespace TaskManager.Desktop.Services;

/// <summary>
/// Entrada con Google en el escritorio: se abre el navegador **del sistema** —Google no admite
/// WebView incrustado— y se espera la vuelta en un servidor local de un solo uso.
///
/// Por eso el nucleo usa PKCE: el codigo llega como parametro de consulta y sí llega al servidor
/// local. Con el flujo implicito el token viajaria en el fragmento, que el navegador nunca envia.
/// </summary>
public sealed class LoopbackOAuthBrowser : IOAuthBrowser
{
    /// <summary>Puerto preferido; si esta ocupado se coge uno libre cualquiera.</summary>
    private const int PreferredPort = 53682;

    private readonly int _port;

    public LoopbackOAuthBrowser() => _port = FindPort();

    /// <summary>
    /// Debe estar dado de alta en Supabase (Authentication › URL Configuration › Redirect URLs).
    /// Conviene registrar `http://127.0.0.1:*/auth/` para que valga aunque cambie el puerto.
    /// </summary>
    public string RedirectUri => $"http://127.0.0.1:{_port}/auth/";

    public async Task<Uri> AuthenticateAsync(Uri authorizeUrl, CancellationToken cancellationToken = default)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri);
        listener.Start();

        Process.Start(new ProcessStartInfo(authorizeUrl.ToString()) { UseShellExecute = true });

        // Sin este registro, cerrar el navegador sin entrar dejaria la espera colgada para siempre.
        using var registration = cancellationToken.Register(listener.Abort);

        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().ConfigureAwait(false);
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException("Entrada cancelada.");
        }

        var uri = context.Request.Url ?? new Uri(RedirectUri);
        await RespondAsync(context).ConfigureAwait(false);
        listener.Stop();

        return uri;
    }

    /// <summary>Pagina de cortesia: el usuario tiene que saber que ya puede volver a la aplicacion.</summary>
    private static async Task RespondAsync(HttpListenerContext context)
    {
        const string html = """
            <!doctype html>
            <html lang="es"><head><meta charset="utf-8"><title>Task Manager</title>
            <style>
              body { font-family: Segoe UI, system-ui, sans-serif; background:#F8F9FA; color:#191C1D;
                     display:flex; align-items:center; justify-content:center; height:100vh; margin:0; }
              .card { background:#fff; border-radius:16px; padding:32px 40px; text-align:center;
                      box-shadow:0 2px 18px rgba(0,0,0,.12); }
              h1 { color:#3525CD; font-size:20px; margin:0 0 8px; }
              p { margin:0; color:#464555; font-size:14px; }
              @media (prefers-color-scheme: dark) {
                body { background:#141318; color:#E6E1E9; }
                .card { background:#201F27; box-shadow:none; }
                h1 { color:#635BF2; } p { color:#C7C4D8; }
              }
            </style></head>
            <body><div class="card"><h1>Ya estás dentro</h1>
            <p>Puedes cerrar esta pestaña y volver a Task Manager.</p></div></body></html>
            """;

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }

    private static int FindPort()
    {
        try
        {
            using var probe = new TcpListener(IPAddress.Loopback, PreferredPort);
            probe.Start();
            probe.Stop();
            return PreferredPort;
        }
        catch (SocketException)
        {
            using var any = new TcpListener(IPAddress.Loopback, 0);
            any.Start();
            var port = ((IPEndPoint)any.LocalEndpoint).Port;
            any.Stop();
            return port;
        }
    }
}

/// <summary>
/// Tokens cifrados con DPAPI, atados al usuario de Windows: un fichero copiado a otro equipo no
/// sirve de nada. Es lo mas parecido a un llavero que hay sin meter dependencias.
/// </summary>
public sealed class DpapiTokenStore : ITokenStore
{
    private readonly string _path;
    private readonly Dictionary<string, string> _values;

    public DpapiTokenStore(string folder)
    {
        _path = Path.Combine(folder, "tokens.dat");
        _values = Load(_path);
    }

    public Task<string?> GetAsync(string key)
    {
        if (!_values.TryGetValue(key, out var protectedValue))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedValue), null, DataProtectionScope.CurrentUser);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }
        catch (CryptographicException)
        {
            // Perfil de Windows distinto o fichero manipulado: se trata como si no hubiera sesion.
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetAsync(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _values.Remove(key);
        }
        else
        {
            var bytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
            _values[key] = Convert.ToBase64String(bytes);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(_values));
        return Task.CompletedTask;
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
