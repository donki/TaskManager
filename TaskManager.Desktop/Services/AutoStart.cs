namespace TaskManager.Desktop.Services;

/// <summary>
/// Inicio con Windows (especificacion 6.A). Se usa la clave Run del usuario: no necesita permisos
/// de administrador y el propio usuario puede quitarlo desde el Administrador de tareas.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskManager";

    public static bool IsEnabled =>
        Sistema.Actual.LeerRegistro(RunKey, ValueName) is string value &&
        value.Contains("TaskManager", StringComparison.OrdinalIgnoreCase);

    public static void Set(bool enabled)
    {
        var executable = Environment.ProcessPath;

        if (!enabled)
        {
            Sistema.Actual.BorrarRegistro(RunKey, ValueName);
        }
        else if (!string.IsNullOrEmpty(executable))
        {
            // --tray: arrancar directamente escondido en la bandeja, sin abrir el panel.
            Sistema.Actual.EscribirRegistro(RunKey, ValueName, $"\"{executable}\" --tray");
        }
    }
}
