using Microsoft.Win32;

namespace TaskManager.Desktop.Services;

/// <summary>
/// Inicio con Windows (especificacion 6.A). Se usa la clave Run del usuario: no necesita permisos
/// de administrador y el propio usuario puede quitarlo desde el Administrador de tareas.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskManager";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Contains("TaskManager", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(executable))
            {
                // --tray: arrancar directamente escondido en la bandeja, sin abrir el panel.
                key.SetValue(ValueName, $"\"{executable}\" --tray");
            }
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
