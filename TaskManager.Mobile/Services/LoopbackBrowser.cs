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
    private TcpListener _listener;

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
    /// El <b>puerto</b> se reserva una vez y vale para todas las entradas que haga la aplicacion.
    /// Uno distinto en cada intento no serviria: <see cref="RedirectUri"/> se lee —y se manda al
    /// navegador— antes de ponerse a escuchar, asi que la vuelta llegaria a un sitio donde ya no
    /// hay nadie. El servidor si se puede rehacer, y se rehace cuando una espera se corta
    /// (<see cref="Reiniciar"/>), pero siempre sobre ese mismo puerto.
    /// </remarks>
    public async Task<Uri> AuthenticateAsync(Uri authorizeUrl, CancellationToken cancellationToken = default)
    {
        // Volver a la aplicacion sin haber terminado ES cancelar.
        //
        // Aqui se espera a que el navegador redirija a la loopback, y hay respuestas que NO
        // redirigen nunca: cuando el proveedor rechaza la peticion —el `unauthorized_client` de
        // Entra del 2026-09-03, por ejemplo— enseña su propia pagina de error y ahi se acaba. Sin
        // esto, la espera no terminaba jamas y la pantalla se quedaba con la rueda girando para
        // siempre, sin manera de salir mas que matando la aplicacion.
        //
        // Se pide que la aplicacion se haya ido de la pantalla ANTES (Stopped) para no confundir
        // con un Resumed suelto: lo que cuenta como abandono es irse al navegador y volver de vacio.
        using var abandonada = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ventana = Application.Current?.Windows.FirstOrDefault();
        var fuera = false;

        void SeVa(object? sender, EventArgs e) => fuera = true;
        void Vuelve(object? sender, EventArgs e)
        {
            if (fuera)
            {
                abandonada.Cancel();
            }
        }

        void DejarDeMirar()
        {
            if (ventana is not null)
            {
                ventana.Stopped -= SeVa;
                ventana.Resumed -= Vuelve;
            }
        }

        if (ventana is not null)
        {
            ventana.Stopped += SeVa;
            ventana.Resumed += Vuelve;
        }

        try
        {
            await Browser.Default.OpenAsync(authorizeUrl, BrowserLaunchMode.SystemPreferred);

            Socket socket;
            try
            {
                socket = await _listener.AcceptSocketAsync(abandonada.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Una espera que se corta deja el servidor en un estado que no se puede dar por
                // bueno: la aceptacion cancelada puede seguir viva por debajo y quedarse con la
                // vuelta del SIGUIENTE intento, que entonces espera para siempre una conexion que
                // ya se ha llevado otro. Paso justo eso: tras dejar a medias una entrada con
                // Microsoft, la siguiente con Google se colgaba.
                //
                // Se rehace en el MISMO puerto, que es lo que no puede cambiar: la redireccion se
                // manda al navegador antes de llegar aqui (RedirectUri) y tiene que seguir
                // apuntando a donde se escucha.
                Reiniciar();
                throw;
            }

            using (socket)
            {

            // Ya ha llegado. A partir de aqui volver a la aplicacion es lo normal —lo hace ella
            // sola con BringToFront— y no puede cancelar nada, asi que se deja de mirar y lo que
            // queda usa la cancelacion de quien llamo.
            DejarDeMirar();

                var target = await ReadRequestTargetAsync(socket, cancellationToken).ConfigureAwait(false);
                await RespondAsync(socket, cancellationToken).ConfigureAwait(false);

                // El navegador se queda delante con la pagina de cortesia: hay que devolver la
                // aplicacion al frente o el usuario se queda mirando una pestaña que ya no hace
                // nada.
                BringToFront();

                return new Uri(new Uri($"http://127.0.0.1:{Port}"), target);
            }
        }
        finally
        {
            DejarDeMirar();
        }
    }

    /// <summary>Levanta otra vez el servidor local, en el mismo puerto de siempre.</summary>
    private void Reiniciar()
    {
        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
            // Ya estaba caido: lo unico que importa es que despues haya uno escuchando.
        }

        _listener = new TcpListener(IPAddress.Loopback, Port);
        _listener.Start();
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
