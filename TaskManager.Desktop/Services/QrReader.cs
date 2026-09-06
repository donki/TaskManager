using System.IO;
using System.Windows.Media.Imaging;
using TaskManager.Core.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TaskManager.Desktop.Services;

/// <summary>
/// Leer el QR de una invitacion en Windows: de una imagen, del portapapeles o de la propia pantalla.
/// </summary>
/// <remarks>
/// <para><b>Por que no hay camara.</b> En un ordenador el QR casi nunca esta delante de una lente:
/// esta en la pantalla del movil de quien invita, en un correo abierto al lado o en la imagen que
/// acaba de llegar por el chat. Por eso se lee de donde de verdad esta —un fichero, lo copiado o lo
/// que se ve— en vez de pedir una webcam que muchos equipos no tienen y que obliga a imprimir el
/// codigo para ponerselo delante.</para>
///
/// <para>Un QR de la pantalla se busca en cada monitor por separado, no en una foto gigante de
/// todos: cuanto mas grande es la imagen, mas pequeño queda el codigo dentro y peor se lee.</para>
/// </remarks>
public static class QrReader
{
    /// <summary>Lee la invitacion de una imagen. <c>null</c> si ahi no hay ningun QR nuestro.</summary>
    public static GroupInvite? Leer(Drawing.Bitmap imagen)
    {
        var lector = new ZXing.Windows.Compatibility.BarcodeReader
        {
            // Girado y del reves tambien: una captura de pantalla sale derecha, pero la foto de un
            // movil enseñando el codigo, no.
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = [ZXing.BarcodeFormat.QR_CODE],
            },
        };

        var leido = lector.Decode(imagen)?.Text;

        return Uri.TryCreate(leido, UriKind.Absolute, out var enlace) ? GroupLink.Read(enlace) : null;
    }

    /// <summary>Lee la invitacion de un fichero de imagen.</summary>
    public static GroupInvite? DesdeFichero(string ruta)
    {
        try
        {
            using var imagen = new Drawing.Bitmap(ruta);
            return Leer(imagen);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
        {
            // Un fichero que no es una imagen, o que esta a medio bajar, no es motivo para reventar.
            return null;
        }
    }

    /// <summary>
    /// Lee la invitacion de lo que haya copiado: la imagen del QR o, si lo que se copio fue el
    /// enlace, el enlace mismo.
    /// </summary>
    public static GroupInvite? DesdePortapapeles()
    {
        if (System.Windows.Clipboard.ContainsText() &&
            Uri.TryCreate(System.Windows.Clipboard.GetText().Trim(), UriKind.Absolute, out var enlace) &&
            GroupLink.Read(enlace) is { } deTexto)
        {
            return deTexto;
        }

        if (!System.Windows.Clipboard.ContainsImage())
        {
            return null;
        }

        using var imagen = AMapaDeBits(System.Windows.Clipboard.GetImage());
        return imagen is null ? null : Leer(imagen);
    }

    /// <summary>Busca el QR en lo que se este viendo, monitor por monitor.</summary>
    public static GroupInvite? DesdePantalla()
    {
        foreach (var pantalla in Forms.Screen.AllScreens)
        {
            var area = pantalla.Bounds;
            using var foto = new Drawing.Bitmap(area.Width, area.Height);

            using (var lienzo = Drawing.Graphics.FromImage(foto))
            {
                lienzo.CopyFromScreen(area.Location, Drawing.Point.Empty, area.Size);
            }

            if (Leer(foto) is { } invite)
            {
                return invite;
            }
        }

        return null;
    }

    /// <summary>
    /// Pasa la imagen del portapapeles (que WPF da a su manera) al mapa de bits que entiende el
    /// lector. Se pasa por PNG porque es el unico camino que no pierde el canal alfa ni los colores.
    /// </summary>
    private static Drawing.Bitmap? AMapaDeBits(BitmapSource? origen)
    {
        if (origen is null)
        {
            return null;
        }

        var codificador = new PngBitmapEncoder();
        codificador.Frames.Add(BitmapFrame.Create(origen));

        using var memoria = new MemoryStream();
        codificador.Save(memoria);
        memoria.Position = 0;

        return new Drawing.Bitmap(memoria);
    }
}
