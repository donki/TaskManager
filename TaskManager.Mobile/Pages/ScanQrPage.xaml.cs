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
    /// <summary>Lo que se espera a que la camara empiece a dar imagen antes de sospechar.</summary>
    private static readonly TimeSpan Paciencia = TimeSpan.FromSeconds(6);

    private readonly TaskCompletionSource<GroupInvite?> _resultado = new();
    private int _entregado;
    private int _imagenes;

    /// <summary>
    /// Lo leido, guardado aparte de la respuesta.
    /// </summary>
    /// <remarks>
    /// <b>Es lo que arregla el fallo de «lo lee y no hace nada».</b> Al salir de la pantalla se
    /// responde con esto, y al leer un codigo se guarda aqui <i>antes</i> de cerrarla. Antes se
    /// respondia <c>null</c> desde <see cref="OnNavigatedFrom"/> y la respuesta buena llegaba
    /// despues: como la primera respuesta es la que vale, la invitacion recien leida se tiraba y la
    /// pantalla de grupos se quedaba sin hacer nada, que es exactamente lo que se veia.
    /// </remarks>
    private GroupInvite? _leido;

    private ScanQrPage()
    {
        InitializeComponent();

        Camara.Options = new BarcodeReaderOptions
        {
            // Solo QR: buscar tambien codigos de barras de una dimension gasta trabajo en algo que
            // esta aplicacion no usa, y confunde mas que ayuda.
            Formats = BarcodeFormat.QrCode,
            AutoRotate = true,
            // Se mira mas fino y tambien en negativo: un QR en la pantalla de otro aparato llega con
            // reflejos, torcido y a veces en claro sobre oscuro, que es como esta esta aplicacion.
            TryHarder = true,
            TryInverted = true,
            Multiple = false,
        };

        Camara.FrameReady += (_, _) => Interlocked.Increment(ref _imagenes);
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

    /// <summary>
    /// Si a los seis segundos no ha llegado ni una imagen, la camara no esta dando nada.
    /// </summary>
    /// <remarks>
    /// Una pantalla negra donde no pasa nada se ve igual cuando no encuentra el codigo que cuando no
    /// hay imagen que mirar. Distinguirlo es la diferencia entre «acercalo mas» y «esto no va».
    /// </remarks>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        Dispatcher.DispatchDelayed(Paciencia, () =>
        {
            if (Volatile.Read(ref _imagenes) == 0)
            {
                HintLabel.Text = Localization.Loc.Instance["ScanNoFrames"];
            }
        });
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

        var texto = e.Results[0].Value;
        var invite = Uri.TryCreate(texto, UriKind.Absolute, out var enlace)
            ? GroupLink.Read(enlace)
            : null;

        Dispatcher.Dispatch(async () =>
        {
            Camara.IsDetecting = false;

            if (invite is null)
            {
                // Un QR cualquiera —el de una wifi, el de un ticket— no es una invitacion. Se dice
                // con lo que ponia dentro, que es lo unico que distingue «he leido otra cosa» de «he
                // leido el nuestro y ha llegado roto», y se sigue mirando: cerrar la camara
                // obligaria a volver a abrirla para el bueno.
                await SocShared.ModernDialog.AlertAsync(
                    this,
                    Localization.Loc.Instance["ScanTitle"],
                    Localization.Loc.Instance["ScanNotOurs"] + Environment.NewLine + Environment.NewLine + texto,
                    Localization.Loc.Instance["Ok"]);

                Interlocked.Exchange(ref _entregado, 0);
                Camara.IsDetecting = true;
                return;
            }

            // Primero se guarda y luego se cierra: al cerrar salta OnNavigatedFrom, que responde con
            // esto mismo. Al reves se perdia la lectura.
            _leido = invite;
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
        _resultado.TrySetResult(_leido);
    }
}
