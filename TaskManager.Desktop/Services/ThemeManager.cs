using System.Windows;
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
