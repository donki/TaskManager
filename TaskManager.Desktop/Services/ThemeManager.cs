using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace TaskManager.Desktop.Services;

/// <summary>
/// Modo claro / oscuro siguiendo a Windows (especificacion 3). Se sustituyen los mismos nombres de
/// recurso que define App.xaml, asi que la interfaz no tiene que saber en que tema esta.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDark { get; private set; }

    public static void Apply()
    {
        IsDark = ReadSystemPrefersDark();

        var resources = Application.Current.Resources;
        if (IsDark)
        {
            Set(resources, "PageBackground", "#141318");
            Set(resources, "CardBackground", "#201F27");
            Set(resources, "SubmenuBackground", "#2A2833");
            Set(resources, "Separator", "#48454F");
            Set(resources, "TextPrimary", "#E6E1E9");
            Set(resources, "TextSecondary", "#C7C4D8");
        }
        else
        {
            Set(resources, "PageBackground", "#F8F9FA");
            Set(resources, "CardBackground", "#FFFFFF");
            Set(resources, "SubmenuBackground", "#EDEEEF");
            Set(resources, "Separator", "#C7C4D8");
            Set(resources, "TextPrimary", "#191C1D");
            Set(resources, "TextSecondary", "#464555");
        }
    }

    /// <summary>
    /// Pinta la barra de titulo del sistema con el fondo de la aplicacion, y sigue al tema.
    /// </summary>
    /// <remarks>
    /// <para>WPF no puede dibujar la barra de titulo: la pinta Windows. Lo unico que se le puede
    /// decir es de que color la quieres, y eso se hace por DWM
    /// (<c>DwmSetWindowAttribute</c>). Sin esto, una ventana con el fondo casi negro llevaba encima
    /// una barra clara del sistema, que era la unica pieza que no seguia el tema.</para>
    ///
    /// <para><b>Se aplica cuando existe el handle</b>, no en el constructor: antes de
    /// <see cref="Window.SourceInitialized"/> la ventana todavia no tiene ventana de verdad y la
    /// llamada no haria nada.</para>
    ///
    /// <para>Es de Windows 11 (build 22000). En versiones anteriores la llamada devuelve error y se
    /// ignora: se queda la barra de siempre, que es exactamente lo que habia antes.</para>
    /// </remarks>
    public static void StyleTitleBar(Window window)
    {
        void Paint()
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            // El orden importa poco, pero el modo oscuro primero deja bien los botones de
            // minimizar/cerrar aunque el color no llegue a aplicarse.
            var dark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));

            var caption = ToColorRef(IsDark ? "#141318" : "#F8F9FA");
            DwmSetWindowAttribute(handle, DwmCaptionColor, ref caption, sizeof(int));

            var text = ToColorRef(IsDark ? "#E6E1E9" : "#191C1D");
            DwmSetWindowAttribute(handle, DwmTextColor, ref text, sizeof(int));
        }

        if (window.IsLoaded || new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Paint();
        }
        else
        {
            window.SourceInitialized += (_, _) => Paint();
        }
    }

    /// <summary>DWM quiere el color al reves que todo el mundo: 0x00BBGGRR.</summary>
    private static int ToColorRef(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        return color.R | (color.G << 8) | (color.B << 16);
    }

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static void Set(ResourceDictionary resources, string key, string hex) =>
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    private static bool ReadSystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            // Si la clave no existe (equipo con politica restrictiva), tema claro y a seguir.
            return false;
        }
    }
}
