using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TaskManager.Desktop.Services;

/// <summary>Que clase de aviso flotante (Growl) se enseña.</summary>
public enum TipoAviso
{
    Exito,
    Advertencia,
    Error,
}

/// <summary>
/// Todo lo que la aplicacion le pide a Windows y que no es suyo: el navegador, el portapapeles, el
/// registro, el cuadro de abrir fichero, los avisos flotantes, el arrastre, la pantalla y el atajo
/// global.
/// </summary>
/// <remarks>
/// <para>Esta detras de una interfaz por las pruebas: el banco ejerce las ventanas de verdad y no
/// puede abrir el navegador, pisar el portapapeles de quien trabaja en el equipo ni escribir en su
/// registro. Aqui no hay logica: cada miembro es una llamada al sistema, y lo que decide que
/// llamar vive en las ventanas, que es lo que se prueba.</para>
/// </remarks>
public interface ISistema
{
    /// <summary>Abre un enlace o un fichero con lo que Windows tenga para eso.</summary>
    void Abrir(string destino);

    bool HayTexto();

    string LeerTexto();

    void EscribirTexto(string texto);

    bool HayImagen();

    BitmapSource? LeerImagen();

    bool HayFicheros();

    IReadOnlyList<string> LeerFicheros();

    /// <summary>El cuadro de abrir fichero. <c>null</c> si se cancela.</summary>
    string? ElegirFichero(Window owner, string? filtro);

    /// <summary>Aviso flotante de HandyControl, que no necesita ventana madre.</summary>
    void Aviso(TipoAviso tipo, string texto);

    /// <summary>Arranca un arrastre y no vuelve hasta soltar.</summary>
    void Arrastrar(DependencyObject origen, object datos);

    /// <summary>Una foto de cada monitor.</summary>
    IReadOnlyList<Drawing.Bitmap> CapturarPantallas();

    /// <summary>Un valor de HKCU, o <c>null</c> si no esta.</summary>
    object? LeerRegistro(string clave, string nombre);

    /// <summary>Escribe un valor de texto en HKCU, creando la clave si hace falta.</summary>
    void EscribirRegistro(string clave, string nombre, string valor);

    void BorrarRegistro(string clave, string nombre);

    bool RegistrarAtajo(IntPtr ventana, int id, uint modificadores, uint tecla);

    void SoltarAtajo(IntPtr ventana, int id);

    /// <summary>Las teclas Ctrl, Alt, Mayus y Windows que esten pulsadas.</summary>
    ModifierKeys Teclas { get; }

    /// <summary>Como esta el boton izquierdo del raton en este evento.</summary>
    MouseButtonState BotonIzquierdo(MouseEventArgs e);

    /// <summary>Donde esta el raton en este evento, respecto a la ventana.</summary>
    Point PosicionRaton(MouseEventArgs e);

    /// <summary>Si el icono de la bandeja se enseña (en las pruebas, no).</summary>
    bool BandejaVisible { get; }
}

/// <summary>El sistema de verdad.</summary>
public sealed class SistemaReal : ISistema
{
    public void Abrir(string destino) =>
        Process.Start(new ProcessStartInfo(destino) { UseShellExecute = true });

    public bool HayTexto() => Clipboard.ContainsText();

    public string LeerTexto() => Clipboard.GetText();

    public void EscribirTexto(string texto) => Clipboard.SetText(texto);

    public bool HayImagen() => Clipboard.ContainsImage();

    public BitmapSource? LeerImagen() => Clipboard.GetImage();

    public bool HayFicheros() => Clipboard.ContainsFileDropList();

    public IReadOnlyList<string> LeerFicheros() => [.. Clipboard.GetFileDropList().Cast<string>()];

    public string? ElegirFichero(Window owner, string? filtro)
    {
        var dialogo = new OpenFileDialog { Multiselect = false, CheckFileExists = true };
        if (filtro is not null)
        {
            dialogo.Filter = filtro;
        }

        return dialogo.ShowDialog(owner) == true ? dialogo.FileName : null;
    }

    public void Aviso(TipoAviso tipo, string texto)
    {
        switch (tipo)
        {
            case TipoAviso.Exito: HandyControl.Controls.Growl.SuccessGlobal(texto); break;
            case TipoAviso.Advertencia: HandyControl.Controls.Growl.WarningGlobal(texto); break;
            default: HandyControl.Controls.Growl.ErrorGlobal(texto); break;
        }
    }

    public void Arrastrar(DependencyObject origen, object datos) =>
        DragDrop.DoDragDrop(origen, datos, DragDropEffects.Move);

    public IReadOnlyList<Drawing.Bitmap> CapturarPantallas()
    {
        var fotos = new List<Drawing.Bitmap>();
        foreach (var pantalla in Forms.Screen.AllScreens)
        {
            var area = pantalla.Bounds;
            var foto = new Drawing.Bitmap(area.Width, area.Height);
            using var lienzo = Drawing.Graphics.FromImage(foto);
            lienzo.CopyFromScreen(area.Location, Drawing.Point.Empty, area.Size);
            fotos.Add(foto);
        }

        return fotos;
    }

    public object? LeerRegistro(string clave, string nombre)
    {
        using var key = Registry.CurrentUser.OpenSubKey(clave);
        return key?.GetValue(nombre);
    }

    public void EscribirRegistro(string clave, string nombre, string valor)
    {
        using var key = Registry.CurrentUser.CreateSubKey(clave);
        key.SetValue(nombre, valor);
    }

    public void BorrarRegistro(string clave, string nombre)
    {
        using var key = Registry.CurrentUser.OpenSubKey(clave, writable: true);
        key?.DeleteValue(nombre, throwOnMissingValue: false);
    }

    public bool RegistrarAtajo(IntPtr ventana, int id, uint modificadores, uint tecla) =>
        RegisterHotKey(ventana, id, modificadores, tecla);

    public void SoltarAtajo(IntPtr ventana, int id) => UnregisterHotKey(ventana, id);

    public ModifierKeys Teclas => Keyboard.Modifiers;

    public MouseButtonState BotonIzquierdo(MouseEventArgs e) => e.LeftButton;

    public Point PosicionRaton(MouseEventArgs e) => e.GetPosition(null);

    public bool BandejaVisible => true;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>El sistema con el que habla la aplicacion. Las pruebas ponen uno de mentira.</summary>
public static class Sistema
{
    public static ISistema Actual { get; set; } = new SistemaReal();
}

/// <summary>
/// Donde vive lo de la aplicacion en este equipo: la carpeta de datos y el nombre de la instancia
/// unica. Las pruebas los cambian para no tocar nunca los de verdad.
/// </summary>
public static class Rutas
{
    /// <summary>%LOCALAPPDATA%\Socratic\TaskManager: base, tokens, buzon de invitaciones y crash.log.</summary>
    public static string Carpeta { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Socratic", "TaskManager");

    /// <summary>
    /// Raiz de los nombres de la instancia unica. Lleva el usuario dentro: con dos sesiones de
    /// Windows abiertas, cada una tiene su aplicacion y su base de datos.
    /// </summary>
    public static string Instancia { get; set; } = $"Socratic.TaskManager.{{0}}.{Environment.UserName}";
}

/// <summary>
/// Por aqui se abren todas las ventanas y se apaga la aplicacion.
/// </summary>
/// <remarks>
/// Es un punto de paso, no una abstraccion: abre la ventana como siempre. Existe para que el banco
/// de pruebas se entere de cada ventana que se abre —la saca de la pantalla y contesta a los
/// dialogos modales— sin que el codigo de las ventanas tenga que saber nada de pruebas.
/// </remarks>
public static class Ventanas
{
    /// <summary>Justo antes de enseñar una ventana. El booleano dice si es modal.</summary>
    public static event Action<Window, bool>? Abriendo;

    /// <summary>Apagar la aplicacion. Las pruebas no pueden apagar el proceso que las corre.</summary>
    public static Action Apagar { get; set; } = () => Application.Current?.Shutdown();

    public static bool? Modal(Window ventana)
    {
        Abriendo?.Invoke(ventana, true);
        return ventana.ShowDialog();
    }

    public static void Mostrar(Window ventana)
    {
        Abriendo?.Invoke(ventana, false);
        ventana.Show();
    }

    /// <summary>
    /// Traer una ventana al frente con el foco. Las pruebas no se lo piden a Windows: el foco de
    /// verdad depende de lo que haya en el escritorio, y el panel se esconde al perderlo.
    /// </summary>
    public static Action<Window> Activar { get; set; } = ventana => ventana.Activate();
}
