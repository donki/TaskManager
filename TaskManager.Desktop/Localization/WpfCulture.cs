using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

// «Calendar» esta en los dos sitios (el control y el calendario juliano/gregoriano de
// System.Globalization, que aqui entra por la cultura); en este fichero siempre es el control.
using Calendar = System.Windows.Controls.Calendar;

namespace TaskManager.Desktop.Localization;

/// <summary>
/// Pone en el idioma de la aplicacion lo que pinta WPF por su cuenta.
/// </summary>
/// <remarks>
/// <para><b>Que se veia mal.</b> El calendario que despliega un selector de fecha —la fila de
/// iniciales de los dias y el nombre del mes— no sale de ningun texto nuestro: lo escribe WPF, y
/// para saber en que idioma no mira la cultura del equipo ni la de la aplicacion, sino la propiedad
/// <see cref="FrameworkElement.Language"/> del propio control.</para>
///
/// <para><b>Y ahi estaba el chino.</b> El estilo que HandyControl le pone al
/// <see cref="Calendar"/> trae <c>Language="zh-cn"</c> escrito dentro. Comprobado en un banco de
/// pruebas con los diccionarios de la libreria cargados: el calendario del desplegable dice
/// <c>zh-cn</c> —con origen <c>Style</c>— mientras que el selector que lo contiene dice
/// <c>en-us</c>. De ahi el «2026年9月» y el «一 二 三 四 五 六» en una ficha en castellano.</para>
///
/// <para><b>Por que no basta con la ventana.</b> El idioma heredado del padre <b>pierde</b> contra
/// el setter de un estilo, que pesa mas en el orden de precedencia de WPF. Lo unico que gana a un
/// estilo es un <b>valor local</b>, escrito sobre el control mismo.</para>
///
/// <para><b>Y por que hay que bajar desde el selector.</b> Escribirselo al calendario cuando el
/// calendario se carga llega tarde: para entonces ya ha generado su fila de dias, y cambiarle el
/// idioma despues no la rehace —se queda el chino en pantalla—. Ademas, el calendario vive dentro
/// del desplegable, que es otra ventana, y no le llegan los manejadores de clase por su cuenta. Lo
/// que si funciona es engancharse a la carga del <b>selector</b>, bajar a su
/// <c>PART_Popup</c> y escribirle el idioma al calendario de dentro <b>antes</b> de que llegue a
/// pintar nada. Comprobado en el mismo banco: pasa a <c>es-es</c> con origen <c>Local</c> y se lee
/// «L M X J V S D».</para>
///
/// <para><b>Pero eso tampoco llegaba a la ficha de la tarea.</b> Ahi los selectores de plazo y de
/// planificacion estan en filas <b>colapsadas</b> hasta que se marca su casilla, y a lo que cuelga
/// de un subarbol colapsado <b>no le llega <c>Loaded</c></b>: el manejador de arriba nunca corria
/// y el calendario salia en chino (visto el 2026-09-12 en la 2026.09.11.1, y reproducido en el
/// banco metiendo el selector en una fila colapsada). Lo que no depende de cargas ni de
/// visibilidad es una <b>coercion</b> de <see cref="FrameworkElement.Language"/> sobre
/// <see cref="CalendarItem"/>, que es la pieza que escribe el mes y las iniciales de los dias:
/// cada vez que WPF vaya a darle un valor —el heredado, el del estilo, el que sea— se le devuelve
/// el idioma de la aplicacion. Sobre <see cref="Calendar"/> no se puede, porque ese ya registra su
/// propia metadata para esa propiedad y WPF no deja hacerlo dos veces. Los manejadores de carga se
/// quedan para el resto de controles y como segunda red.</para>
///
/// <para>De paso se fija la cultura de los hilos, que es la que decide como se escribe una fecha o
/// un numero cuando se dan por texto.</para>
/// </remarks>
internal static class WpfCulture
{
    private static bool _installed;

    /// <summary>Se llama una vez, al arrancar, y otra vez cada vez que se cambia el idioma.</summary>
    public static void Install()
    {
        var culture = Resolve();

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        // Las ventanas que ya estan abiertas no pasan otra vez por «cargada»: se les cambia aqui,
        // y a sus calendarios se les vuelve a pedir el idioma para que la coercion de abajo actue.
        foreach (var window in Application.Current?.Windows.OfType<Window>() ?? [])
        {
            Apply(window);

            foreach (var item in Descendants<CalendarItem>(window))
            {
                item.CoerceValue(FrameworkElement.LanguageProperty);
            }
        }

        if (_installed)
        {
            return;
        }

        _installed = true;

        // La pieza del calendario que escribe el mes y los dias: se le impone el idioma por
        // coercion, que no depende de que el control llegue a cargarse ni de que este visible.
        try
        {
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(CalendarItem),
                new FrameworkPropertyMetadata { CoerceValueCallback = (_, _) => Language() });
        }
        catch (ArgumentException ex)
        {
            // Solo puede fallar si ya existiera un CalendarItem antes de llegar aqui: no pasa,
            // porque esto corre antes de crear ninguna ventana, pero un texto no tumba el arranque.
            System.Diagnostics.Debug.WriteLine($"Idioma del calendario: {ex.Message}");
        }

        // La ventana, para todo lo que herede de ella; y ademas, uno por uno, los controles que
        // traen el idioma escrito en su estilo y que por eso no lo heredan.
        Watch(typeof(Window));
        Watch(typeof(Calendar));
        Watch(typeof(DatePicker));
        Watch(typeof(HandyControl.Controls.TimePicker));
        Watch(typeof(HandyControl.Controls.DateTimePicker));
        Watch(typeof(HandyControl.Controls.CalendarWithClock));
    }

    /// <summary>Le escribe el idioma a cada elemento de ese tipo en cuanto se carga.</summary>
    private static void Watch(Type type) =>
        EventManager.RegisterClassHandler(
            type,
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is not FrameworkElement element)
                {
                    return;
                }

                Apply(element);

                // Los calendarios que cuelgan del control: los que se ven directamente...
                foreach (var calendar in Calendars(element))
                {
                    Apply(calendar);
                }

                // ...y el del desplegable, que vive en otra ventana y no se alcanza por el arbol.
                if (element is DatePicker picker)
                {
                    picker.ApplyTemplate();

                    if (picker.Template?.FindName("PART_Popup", picker) is Popup { Child: { } child })
                    {
                        foreach (var calendar in Calendars(child))
                        {
                            Apply(calendar);
                        }
                    }
                }
            }));

    /// <summary>Los calendarios que haya colgando, por el arbol visual.</summary>
    private static IEnumerable<Calendar> Calendars(DependencyObject root) => Descendants<Calendar>(root);

    /// <summary>Los elementos de ese tipo que haya colgando, por el arbol visual (sin entrar en ellos).</summary>
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found)
        {
            yield return found;
            yield break;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (var item in Descendants<T>(VisualTreeHelper.GetChild(root, i)))
            {
                yield return item;
            }
        }
    }

    /// <summary>La cultura del idioma elegido, con la del equipo como red de seguridad.</summary>
    /// <remarks>
    /// El idioma se guarda como «es» o «en» a secas, y una cultura neutra no trae ni formato de
    /// fecha ni primer dia de la semana. Se pide la especifica que le corresponda —«es-ES»,
    /// «en-US»—, que es la que sabe escribir un lunes.
    /// </remarks>
    private static CultureInfo Resolve()
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(Loc.Language);
            return culture.IsNeutralCulture ? CultureInfo.CreateSpecificCulture(culture.Name) : culture;
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.CurrentCulture;
        }
    }

    private static XmlLanguage Language() => XmlLanguage.GetLanguage(Resolve().IetfLanguageTag);

    private static void Apply(FrameworkElement element) => element.Language = Language();
}
