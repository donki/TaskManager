using TaskManager.Core.Services;
using ZXing.Net.Maui;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// La camara leyendo el QR de una invitacion.
/// </summary>
/// <remarks>
/// <para><b>Por que hace falta.</b> El QR se pinta desde el primer dia, pero no habia con que
/// leerlo: los lectores de codigos del movil abren direcciones web, y una invitacion no es una
/// direccion web sino un enlace propio (<c>taskmanager://join</c>) que solo entiende esta
/// aplicacion. El lector tiene que estar aqui dentro.</para>
///
/// <para>Se devuelve la invitacion al que abrio la pantalla, en vez de entrar en el grupo desde
/// aqui: entrar es cosa de la pantalla de grupos, que ya sabe preguntar, recargar y contar lo que
/// ha pasado. Esta solo mira por la camara.</para>
/// </remarks>
public partial class ScanQrPage : ContentPage
{
    private readonly TaskCompletionSource<GroupInvite?> _resultado = new();
    private int _entregado;

    private ScanQrPage()
    {
        InitializeComponent();

        Camara.Options = new BarcodeReaderOptions
        {
            // Solo QR: buscar tambien codigos de barras de una dimension gasta trabajo en algo que
            // esta aplicacion no usa, y confunde mas que ayuda.
            Formats = BarcodeFormat.QrCode,
            AutoRotate = true,
            Multiple = false,
        };
    }

    /// <summary>
    /// Abre la camara y espera. Devuelve la invitacion leida, o <c>null</c> si se cerro sin leer
    /// nada o si no hay permiso de camara.
    /// </summary>
    public static async Task<GroupInvite?> PedirAsync(Page origen)
    {
        var permiso = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (permiso != PermissionStatus.Granted)
        {
            permiso = await Permissions.RequestAsync<Permissions.Camera>();
        }

        if (permiso != PermissionStatus.Granted)
        {
            // Sin camara no hay lectura, pero el codigo y la clave se pueden teclear: se dice, en
            // vez de dejar una pantalla en negro sin explicacion.
            await SocShared.ModernDialog.AlertAsync(
                origen,
                Localization.Loc.Instance["ScanTitle"],
                Localization.Loc.Instance["ScanNoCamera"],
                Localization.Loc.Instance["Ok"]);

            return null;
        }

        var pagina = new ScanQrPage();
        await origen.Navigation.PushAsync(pagina);

        return await pagina._resultado.Task;
    }

    /// <remarks>
    /// El aviso llega desde el hilo de la camara y puede llegar <b>varias veces</b> con el mismo
    /// codigo delante: se atiende una sola vez (<see cref="_entregado"/>), porque si no se apilan
    /// tantas vueltas atras como fotogramas haya visto.
    /// </remarks>
    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (e.Results.Length == 0 || Interlocked.Exchange(ref _entregado, 1) == 1)
        {
            return;
        }

        var invite = Uri.TryCreate(e.Results[0].Value, UriKind.Absolute, out var enlace)
            ? GroupLink.Read(enlace)
            : null;

        Dispatcher.Dispatch(async () =>
        {
            Camara.IsDetecting = false;

            if (invite is null)
            {
                // Un QR cualquiera —el de una wifi, el de un ticket— no es una invitacion. Se dice y
                // se sigue mirando: cerrar la camara obligaria a volver a abrirla para el bueno.
                await SocShared.ModernDialog.AlertAsync(
                    this,
                    Localization.Loc.Instance["ScanTitle"],
                    Localization.Loc.Instance["ScanNotOurs"],
                    Localization.Loc.Instance["Ok"]);

                Interlocked.Exchange(ref _entregado, 0);
                Camara.IsDetecting = true;
                return;
            }

            await Navigation.PopAsync();
            _resultado.TrySetResult(invite);
        });
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        Camara.IsDetecting = false;
        await Navigation.PopAsync();
    }

    /// <summary>
    /// Se responde tambien al salir con la flecha de volver o con el gesto del sistema: sin esto, el
    /// que abrio la pantalla se quedaria esperando una lectura que ya no va a llegar nunca.
    /// </summary>
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        _resultado.TrySetResult(null);
    }
}
