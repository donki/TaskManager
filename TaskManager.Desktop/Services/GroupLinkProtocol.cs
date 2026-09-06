using System.IO;
using Microsoft.Win32;
using TaskManager.Core.Services;

namespace TaskManager.Desktop.Services;

/// <summary>
/// Hace que Windows entienda los enlaces <c>taskmanager://join?…</c> de las invitaciones a grupos.
/// </summary>
/// <remarks>
/// <para><b>Se da de alta en el usuario, no en la maquina</b> (<c>HKCU\Software\Classes</c>): no
/// pide permisos de administrador, y esta aplicacion se entrega copiando un .exe a una carpeta, sin
/// instalador que pueda hacerlo por su cuenta. Se reescribe en cada arranque porque el .exe cambia
/// de sitio con solo moverlo.</para>
///
/// <para><b>Como llega el enlace.</b> Windows abre otra copia del programa pasandole la direccion
/// como argumento. Como solo puede haber una aplicacion a la vez, esa copia deja la invitacion
/// escrita en un fichero y avisa a la que ya esta corriendo, que es quien la usa: pasarla por
/// argumentos no serviria, porque el proceso que los recibe es el que se apaga.</para>
/// </remarks>
public static class GroupLinkProtocol
{
    /// <summary>Donde la copia nueva deja la invitacion para la que ya estaba abierta.</summary>
    public static string BuzonPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Socratic", "TaskManager", "invitacion.link");

    /// <summary>
    /// Deja el esquema apuntando a este ejecutable. Si algo falla, se traga: no poder abrir
    /// enlaces es un incordio, no una razon para que la aplicacion no arranque.
    /// </summary>
    public static void Registrar()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                return;
            }

            using var clave = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\{GroupLink.Scheme}");

            clave.SetValue(string.Empty, "URL:Task Manager");
            clave.SetValue("URL Protocol", string.Empty);

            using var icono = clave.CreateSubKey("DefaultIcon");
            icono.SetValue(string.Empty, $"\"{exe}\",0");

            using var comando = clave.CreateSubKey(@"shell\open\command");
            comando.SetValue(string.Empty, $"\"{exe}\" \"%1\"");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Protocolo: {ex.Message}");
        }
    }

    /// <summary>La invitacion que venga en los argumentos, o <c>null</c> si no hay ninguna.</summary>
    public static GroupInvite? EnLosArgumentos(string[] args)
    {
        foreach (var arg in args)
        {
            if (Uri.TryCreate(arg, UriKind.Absolute, out var uri) && GroupLink.Read(uri) is { } invite)
            {
                return invite;
            }
        }

        return null;
    }

    /// <summary>Deja la invitacion para la copia que ya esta corriendo.</summary>
    public static void Dejar(GroupInvite invite)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BuzonPath)!);
            File.WriteAllText(BuzonPath, GroupLink.For(invite).ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Buzon: {ex.Message}");
        }
    }

    /// <summary>
    /// Recoge lo que hubiera dejado otra copia, y lo borra: una invitacion se usa una vez.
    /// </summary>
    public static GroupInvite? Recoger()
    {
        try
        {
            if (!File.Exists(BuzonPath))
            {
                return null;
            }

            var texto = File.ReadAllText(BuzonPath);
            File.Delete(BuzonPath);

            return Uri.TryCreate(texto.Trim(), UriKind.Absolute, out var uri) ? GroupLink.Read(uri) : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Buzon: {ex.Message}");
            return null;
        }
    }
}
