using System.Net;
using System.Net.Sockets;
using System.Text;
using TaskManager.Core.Services;

namespace TaskManager.Mobile.Services;

/// <summary>
/// Entrada con Google en Android usando el <b>mismo cliente OAuth que Windows</b>: se abre el
/// navegador del sistema y se recoge la vuelta en un servidor local.
/// </summary>
/// <remarks>
/// <para><b>Por que existe.</b> El camino normal en Android es un cliente OAuth de tipo Android y
/// el esquema de identificador invertido. Ese cliente valida el <i>nombre de paquete</i> y la
/// <i>huella SHA-1</i> de la firma, y si alguna de las dos no coincide con lo dado de alta en Google
/// Cloud, Google contesta <c>Error 400: invalid_request</c> antes de enseñar ninguna cuenta — sin
/// decir cual de las dos falla. Comprobado en el Xiaomi el 2026-08-31.</para>
///
/// <para><b>Que hace esto en su lugar.</b> Usa el cliente de <i>escritorio</i>, que no valida
/// paquete ni firma sino la redireccion a <c>127.0.0.1</c>, y que ya funciona. La aplicacion levanta
/// un servidor minimo en la loopback, el navegador redirige ahi con el codigo, y a partir de ese
/// punto el flujo es exactamente el mismo que en Windows.</para>
///
/// <para><b>Por que a mano y no con <c>HttpListener</c>.</b> <c>HttpListener</c> no esta soportado
/// en Android: lanza <c>PlatformNotSupportedException</c>. Aqui hace falta leer una linea de
/// peticion y contestar una pagina; eso cabe en un <c>TcpListener</c> sin traer ninguna dependencia,
/// que es lo que pide la regla MIT/monetizable.</para>
///
/// <para>Solo escucha en <c>127.0.0.1</c>: ningun otro aparato de la red puede siquiera abrir la
/// conexion. Y el codigo que llega por ahi no vale por si solo: sin el verificador PKCE, que nunca
/// sale de la aplicacion, Google no lo canjea por nada.</para>
/// </remarks>
public sealed class AndroidLoopbackBrowser : IOAuthBrowser
{
    private readonly TcpListener _listener;

    public AndroidLoopbackBrowser()
    {
        // Puerto que da el sistema: dos entradas seguidas no pueden chocar, y no hay que registrar
        // ninguno concreto porque el cliente de escritorio admite cualquiera en la loopback.
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    public string RedirectUri => $"http://127.0.0.1:{Port}/auth/";

    /// <remarks>
    /// El servidor <b>no se cierra</b> al terminar: el puerto se reserva una vez y vale para todas
    /// las entradas que haga la aplicacion. Cerrarlo daria un puerto distinto en cada intento, y
    /// entonces <see cref="RedirectUri"/> —que se lee antes de abrir el navegador— dejaria de
    /// coincidir con el sitio donde se espera la vuelta.
    /// </remarks>
    public async Task<Uri> AuthenticateAsync(Uri authorizeUrl, CancellationToken cancellationToken = default)
    {
        await Browser.Default.OpenAsync(authorizeUrl, BrowserLaunchMode.SystemPreferred);

        using var socket = await _listener.AcceptSocketAsync(cancellationToken).ConfigureAwait(false);
        var target = await ReadRequestTargetAsync(socket, cancellationToken).ConfigureAwait(false);
        await RespondAsync(socket, cancellationToken).ConfigureAwait(false);

        // El navegador se queda delante con la pagina de cortesia: hay que devolver la aplicacion
        // al frente o el usuario se queda mirando una pestaña que ya no hace nada.
        BringToFront();

        return new Uri(new Uri($"http://127.0.0.1:{Port}"), target);
    }

    /// <summary>
    /// Lee solo la primera linea (<c>GET /auth/?code=... HTTP/1.1</c>), que es donde viaja todo lo
    /// que interesa. El resto de la peticion se ignora a proposito: leerla entera obligaria a
    /// interpretar cabeceras y no aporta nada.
    /// </summary>
    private static async Task<string> ReadRequestTargetAsync(Socket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var read = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        var request = Encoding.ASCII.GetString(buffer, 0, read);

        var line = request.Split('\r', '\n')[0];
        var parts = line.Split(' ');

        return parts.Length >= 2 ? parts[1] : "/auth/";
    }

    private static async Task RespondAsync(Socket socket, CancellationToken cancellationToken)
    {
        const string body = """
            <!doctype html>
            <html lang="es"><head><meta charset="utf-8"><title>Task Manager</title>
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <style>
              body { font-family: system-ui, sans-serif; background:#F8F9FA; color:#191C1D;
                     display:flex; align-items:center; justify-content:center; height:100vh; margin:0; }
              .card { background:#fff; border-radius:16px; padding:28px 32px; text-align:center;
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

        var bytes = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bytes.Length}\r\n" +
            "Connection: close\r\n\r\n");

        await socket.SendAsync(header, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        await socket.SendAsync(bytes, SocketFlags.None, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Devuelve la aplicacion al frente sin crear otra actividad: <c>ReorderToFront</c> sube la que
    /// ya estaba, de modo que la pantalla de entrada sigue siendo la misma y no se pierde el estado.
    /// </summary>
    private static void BringToFront()
    {
#if ANDROID
        var context = Android.App.Application.Context;
        var intent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        if (intent is null)
        {
            return;
        }

        intent.AddFlags(Android.Content.ActivityFlags.ReorderToFront | Android.Content.ActivityFlags.NewTask);
        context.StartActivity(intent);
#endif
    }
}
