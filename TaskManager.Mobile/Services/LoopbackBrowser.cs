using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using TaskManager.Core.Services;

namespace TaskManager.Mobile.Services;

/// <summary>
/// Entrada con cuenta en Android usando el <b>mismo cliente OAuth que Windows</b>: se abre el
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
/// <para><b>El servidor escucha SIEMPRE</b>, no solo mientras se espera una entrada. Antes se
/// aceptaba una conexion por intento y se cancelaba esa aceptacion al abandonar: si la vuelta del
/// navegador llegaba despues —y llega, porque el proveedor redirige cuando le toca— nadie la
/// recogia, y el navegador se quedaba colgado para siempre en la pagina de «volviendo a la
/// aplicacion» con la conexion abierta y sin respuesta. Contestando siempre, una vuelta tardia se
/// responde igual y como mucho se descarta; el navegador nunca se queda esperando.</para>
///
/// <para><b>Por que a mano y no con <c>HttpListener</c>.</b> <c>HttpListener</c> no esta soportado
/// en Android: lanza <c>PlatformNotSupportedException</c>. Aqui hace falta leer una linea de
/// peticion y contestar una pagina; eso cabe en un <c>TcpListener</c> sin traer ninguna dependencia,
/// que es lo que pide la regla MIT/monetizable.</para>
///
/// <para>Solo escucha en <c>127.0.0.1</c>: ningun otro aparato de la red puede siquiera abrir la
/// conexion. Y el codigo que llega por ahi no vale por si solo: sin el verificador PKCE, que nunca
/// sale de la aplicacion, el proveedor no lo canjea por nada.</para>
/// </remarks>
public sealed class AndroidLoopbackBrowser : IOAuthBrowser
{
    /// <summary>Lo que el usuario tarda en volver de verdad, no de rebote. Ver <c>Vigilante</c>.</summary>
    private static readonly TimeSpan Rebote = TimeSpan.FromSeconds(2);

    private readonly TcpListener _listener;

    /// <summary>Las vueltas del navegador segun llegan. Sin tope: son una cada muchos minutos.</summary>
    private readonly Channel<Uri> _vueltas = Channel.CreateUnbounded<Uri>();

    public AndroidLoopbackBrowser()
    {
        // Puerto que da el sistema: no hay que registrar ninguno concreto porque el cliente de
        // escritorio admite cualquiera en la loopback. Se reserva UNA vez y vale para todas las
        // entradas: RedirectUri se lee —y se le manda al navegador— antes de esperar la vuelta, asi
        // que un puerto distinto en cada intento mandaria al navegador a un sitio donde no hay nadie.
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _ = Task.Run(EscucharSiempreAsync);
    }

    public int Port { get; }

    public string RedirectUri => $"http://127.0.0.1:{Port}/auth/";

    public async Task<Uri> AuthenticateAsync(Uri authorizeUrl, CancellationToken cancellationToken = default)
    {
        // Lo que hubiera quedado de un intento anterior no vale para este: el codigo de una entrada
        // abandonada es de otra peticion y otro verificador PKCE.
        while (_vueltas.Reader.TryRead(out _))
        {
        }

        using var abandonada = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var vigilante = new Vigilante(abandonada);

        await Browser.Default.OpenAsync(authorizeUrl, BrowserLaunchMode.SystemPreferred);

        var vuelta = await _vueltas.Reader.ReadAsync(abandonada.Token).ConfigureAwait(false);

        // El navegador se queda delante con la pagina de cortesia: hay que devolver la aplicacion
        // al frente o el usuario se queda mirando una pestaña que ya no hace nada.
        BringToFront();

        return vuelta;
    }

    /// <summary>
    /// Acepta conexiones mientras viva la aplicacion y contesta a todas.
    /// </summary>
    /// <remarks>
    /// Un fallo suelto —una conexion que se corta, una peticion rara— no puede tumbar el servidor:
    /// se traga y se sigue escuchando, porque si esto se para la entrada deja de funcionar hasta
    /// que se reinicie la aplicacion. Solo se sale si el propio servidor deja de existir.
    /// </remarks>
    private async Task EscucharSiempreAsync()
    {
        while (true)
        {
            Socket socket;
            try
            {
                socket = await _listener.AcceptSocketAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            using (socket)
            {
                try
                {
                    var target = await ReadRequestTargetAsync(socket).ConfigureAwait(false);
                    await RespondAsync(socket).ConfigureAwait(false);

                    await _vueltas.Writer.WriteAsync(
                        new Uri(new Uri($"http://127.0.0.1:{Port}"), target)).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Esta conexion se pierde; el servidor sigue en pie.
                }
            }
        }
    }

    /// <summary>
    /// Da por abandonada la entrada cuando el usuario <b>vuelve a la aplicacion</b> sin haber
    /// terminado en el navegador.
    /// </summary>
    /// <remarks>
    /// <para>Hace falta porque hay respuestas que no redirigen nunca: cuando el proveedor rechaza la
    /// peticion enseña su propia pagina de error y ahi se acaba. Sin esto, la espera no terminaba
    /// jamas y la pantalla se quedaba con la rueda girando.</para>
    ///
    /// <para><b>Con margen</b> (<see cref="Rebote"/>): se exige que la aplicacion se haya ido antes
    /// —o sea, que el navegador llegara a taparla— y que al volver <i>se quede</i>. Durante la
    /// entrada hay parpadeos en los que la aplicacion se reanuda un instante y el navegador vuelve
    /// enseguida; tomando cualquiera de esos por un abandono se cancelaba una entrada que iba bien.</para>
    /// </remarks>
    private sealed class Vigilante : IDisposable
    {
        private readonly CancellationTokenSource _abandonada;
        private readonly Window? _ventana;
        private bool _fuera;
        private bool _delante;

        public Vigilante(CancellationTokenSource abandonada)
        {
            _abandonada = abandonada;
            _ventana = Application.Current?.Windows.FirstOrDefault();

            if (_ventana is null)
            {
                return;
            }

            _ventana.Stopped += SeVa;
            _ventana.Resumed += Vuelve;
        }

        public void Dispose()
        {
            if (_ventana is null)
            {
                return;
            }

            _ventana.Stopped -= SeVa;
            _ventana.Resumed -= Vuelve;
        }

        private void SeVa(object? sender, EventArgs e)
        {
            _fuera = true;
            _delante = false;
        }

        private void Vuelve(object? sender, EventArgs e)
        {
            if (!_fuera)
            {
                return;
            }

            _delante = true;
            _ = EsperarYDecidirAsync();
        }

        private async Task EsperarYDecidirAsync()
        {
            await Task.Delay(Rebote).ConfigureAwait(false);

            // Si el navegador ha vuelto a taparla, no era un abandono sino un parpadeo.
            if (_delante && !_abandonada.IsCancellationRequested)
            {
                _abandonada.Cancel();
            }
        }
    }

    /// <summary>
    /// Lee solo la primera linea (<c>GET /auth/?code=... HTTP/1.1</c>), que es donde viaja todo lo
    /// que interesa. El resto de la peticion se ignora a proposito: leerla entera obligaria a
    /// interpretar cabeceras y no aporta nada.
    /// </summary>
    private static async Task<string> ReadRequestTargetAsync(Socket socket)
    {
        var buffer = new byte[4096];
        var read = await socket.ReceiveAsync(buffer, SocketFlags.None).ConfigureAwait(false);
        var request = Encoding.ASCII.GetString(buffer, 0, read);

        var line = request.Split('\r', '\n')[0];
        var parts = line.Split(' ');

        return parts.Length >= 2 ? parts[1] : "/auth/";
    }

    private static async Task RespondAsync(Socket socket)
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

        await socket.SendAsync(header, SocketFlags.None).ConfigureAwait(false);
        await socket.SendAsync(bytes, SocketFlags.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Devuelve la aplicacion al frente y <b>cierra la pestaña del navegador</b>.
    /// </summary>
    /// <remarks>
    /// <para>La pestaña de Chrome se abre DENTRO de la misma tarea que la aplicacion, encima de su
    /// actividad. Con <c>ReorderToFront</c> a secas la actividad subia por debajo y la pestaña se
    /// quedaba delante, enseñando la pagina de cortesia: la entrada habia terminado bien y el
    /// usuario se quedaba mirando el navegador, con toda la pinta de que no habia pasado nada
    /// (visto en la tablet el 2026-09-04).</para>
    ///
    /// <para><c>ClearTop</c> termina lo que haya por encima de la actividad —o sea, la pestaña— y
    /// <c>SingleTop</c> evita volver a crearla: como <c>MainActivity</c> ya es <c>SingleTop</c>,
    /// llega por <c>OnNewIntent</c> y no se pierde el estado.</para>
    /// </remarks>
    private static void BringToFront()
    {
#if ANDROID
        var context = Android.App.Application.Context;
        var intent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        if (intent is null)
        {
            return;
        }

        intent.AddFlags(
            Android.Content.ActivityFlags.ClearTop |
            Android.Content.ActivityFlags.SingleTop |
            Android.Content.ActivityFlags.NewTask);
        context.StartActivity(intent);
#endif
    }
}
