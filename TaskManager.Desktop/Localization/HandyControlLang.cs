using System.Globalization;
using System.Reflection;
using System.Resources;

namespace TaskManager.Desktop.Localization;

/// <summary>
/// Pone en castellano (o en ingles) los textos propios de HandyControl.
/// </summary>
/// <remarks>
/// <para><b>Por que hace falta.</b> HandyControl 3.5.1 trae sus 40 cadenas <b>en chino</b> y el
/// paquete no incluye ningun otro idioma: ni satelites de cultura ni un juego neutro en ingles. Asi
/// que cualquier texto que ponga la libreria por su cuenta salia en chino dentro de una aplicacion
/// en castellano — el «a. m.»/«p. m.» del selector de hora, el «no hay datos» de un desplegable
/// vacio, los botones de un aviso emergente y los mensajes de validacion.</para>
///
/// <para><b>Como se hace.</b> La clase <c>Lang</c> de HandyControl guarda su
/// <see cref="ResourceManager"/> en un campo estatico privado. Se sustituye por este, que responde
/// de la tabla de abajo y, para lo que no este traducido, delega en el original. Es un cambio de una
/// linea en el arranque y no toca la libreria: la licencia es MIT, pero aun asi es mejor no tener
/// una copia parcheada que mantener.</para>
///
/// <para><b>Se decide en cada consulta</b>, no al instalarse: el idioma de la aplicacion se puede
/// cambiar en caliente y asi no hay que volver a instalar nada.</para>
/// </remarks>
internal sealed class HandyControlLang : ResourceManager
{
    private readonly ResourceManager _original;

    private HandyControlLang(ResourceManager original) => _original = original;

    /// <summary>
    /// Sustituye el diccionario de HandyControl. Se llama una vez, antes de crear ninguna ventana.
    /// </summary>
    /// <remarks>
    /// Si algun dia la libreria cambia por dentro y el campo ya no esta, no pasa nada: se queda como
    /// estaba —en chino, como hasta ahora— en vez de tirar abajo el arranque por un texto.
    /// </remarks>
    public static void Install()
    {
        try
        {
            var lang = typeof(HandyControl.Controls.Growl).Assembly
                .GetType("HandyControl.Properties.Langs.Lang");

            if (lang?.GetProperty("ResourceManager", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) is not ResourceManager original)
            {
                return;
            }

            lang.GetField("resourceMan", BindingFlags.NonPublic | BindingFlags.Static)
                ?.SetValue(null, new HandyControlLang(original));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HandyControl en castellano: {ex.Message}");
        }
    }

    public override string? GetString(string name) => GetString(name, null);

    public override string? GetString(string name, CultureInfo? culture)
    {
        var tabla = Loc.Language.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? Castellano
            : Ingles;

        return tabla.TryGetValue(name, out var texto) ? texto : _original.GetString(name, culture);
    }

    // -----------------------------------------------------------------------

    private static readonly Dictionary<string, string> Castellano = new(StringComparer.Ordinal)
    {
        ["All"] = "Todas",
        ["Am"] = "a. m.",
        ["Cancel"] = "Cancelar",
        ["Clear"] = "Vaciar",
        ["Close"] = "Cerrar",
        ["CloseAll"] = "Cerrar todo",
        ["CloseOther"] = "Cerrar las demás",
        ["Confirm"] = "Aceptar",
        ["ErrorImgPath"] = "La ruta de la imagen no es correcta",
        ["ErrorImgSize"] = "El tamaño de la imagen no es válido",
        ["Find"] = "Buscar",
        ["FormatError"] = "El formato no es correcto",
        ["Interval10m"] = "Cada 10 minutos",
        ["Interval1h"] = "Cada hora",
        ["Interval1m"] = "Cada minuto",
        ["Interval2h"] = "Cada 2 horas",
        ["Interval30m"] = "Cada 30 minutos",
        ["Interval30s"] = "Cada 30 segundos",
        ["Interval5m"] = "Cada 5 minutos",
        ["IsNecessary"] = "No puede quedar vacío",
        ["Jump"] = "Ir a",
        ["Miscellaneous"] = "Varios",
        ["NextPage"] = "Página siguiente",
        ["No"] = "No",
        ["NoData"] = "No hay nada",
        ["OutOfRange"] = "Fuera del intervalo",
        ["PageMode"] = "Por páginas",
        ["Pm"] = "p. m.",
        ["PngImg"] = "Imagen PNG",
        ["PreviousPage"] = "Página anterior",
        ["ScrollMode"] = "Continuo",
        ["Tip"] = "Aviso",
        ["TooLarge"] = "Demasiado grande",
        ["TwoPageMode"] = "Dos páginas",
        ["Unknown"] = "Desconocido",
        ["UnknownSize"] = "Tamaño desconocido",
        ["Yes"] = "Sí",
        ["ZoomIn"] = "Acercar",
        ["ZoomOut"] = "Alejar",
    };

    private static readonly Dictionary<string, string> Ingles = new(StringComparer.Ordinal)
    {
        ["All"] = "All",
        ["Am"] = "AM",
        ["Cancel"] = "Cancel",
        ["Clear"] = "Clear",
        ["Close"] = "Close",
        ["CloseAll"] = "Close all",
        ["CloseOther"] = "Close the others",
        ["Confirm"] = "OK",
        ["ErrorImgPath"] = "Wrong image path",
        ["ErrorImgSize"] = "Invalid image size",
        ["Find"] = "Find",
        ["FormatError"] = "Wrong format",
        ["Interval10m"] = "Every 10 minutes",
        ["Interval1h"] = "Every hour",
        ["Interval1m"] = "Every minute",
        ["Interval2h"] = "Every 2 hours",
        ["Interval30m"] = "Every 30 minutes",
        ["Interval30s"] = "Every 30 seconds",
        ["Interval5m"] = "Every 5 minutes",
        ["IsNecessary"] = "Cannot be empty",
        ["Jump"] = "Go to",
        ["Miscellaneous"] = "Miscellaneous",
        ["NextPage"] = "Next page",
        ["No"] = "No",
        ["NoData"] = "Nothing here",
        ["OutOfRange"] = "Out of range",
        ["PageMode"] = "Pages",
        ["Pm"] = "PM",
        ["PngImg"] = "PNG image",
        ["PreviousPage"] = "Previous page",
        ["ScrollMode"] = "Continuous",
        ["Tip"] = "Notice",
        ["TooLarge"] = "Too large",
        ["TwoPageMode"] = "Two pages",
        ["Unknown"] = "Unknown",
        ["UnknownSize"] = "Unknown size",
        ["Yes"] = "Yes",
        ["ZoomIn"] = "Zoom in",
        ["ZoomOut"] = "Zoom out",
    };
}
