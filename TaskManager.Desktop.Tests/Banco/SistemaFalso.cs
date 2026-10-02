using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using TaskManager.Desktop.Services;
using Drawing = System.Drawing;

namespace TaskManager.Desktop.Tests.Banco;

/// <summary>
/// Windows de mentira: apunta lo que la aplicacion le pide y devuelve lo que la prueba le dice. Ni
/// navegador, ni portapapeles, ni registro, ni atajos de verdad.
/// </summary>
internal sealed class SistemaFalso : ISistema
{
    public List<string> Abiertos { get; } = [];

    /// <summary>Si no es nulo, abrir algo falla con esto (no hay navegador, por ejemplo).</summary>
    public Exception? FalloAlAbrir { get; set; }

    /// <summary>Lo que hace el «navegador» al abrir una direccion.</summary>
    public Action<string>? AlAbrir { get; set; }

    public void Abrir(string destino)
    {
        if (FalloAlAbrir is { } fallo)
        {
            throw fallo;
        }

        Abiertos.Add(destino);
        AlAbrir?.Invoke(destino);
    }

    // Portapapeles -------------------------------------------------------------

    public string? Texto { get; set; }

    public BitmapSource? Imagen { get; set; }

    public List<string>? Ficheros { get; set; }

    public bool HayTexto() => Texto is not null;

    public string LeerTexto() => Texto ?? string.Empty;

    public void EscribirTexto(string texto) => Texto = texto;

    public bool HayImagen() => Imagen is not null;

    public BitmapSource? LeerImagen() => Imagen;

    public bool HayFicheros() => Ficheros is not null;

    /// <summary>Si no es nulo, leer ficheros del portapapeles falla con esto (otro programa lo tiene cogido).</summary>
    public Exception? FalloPortapapeles { get; set; }

    public IReadOnlyList<string> LeerFicheros() => FalloPortapapeles is { } f ? throw f : Ficheros ?? [];

    // Cuadros y avisos ----------------------------------------------------------

    /// <summary>Lo que se «elige» en cada cuadro de abrir fichero; null es cancelar.</summary>
    public Queue<string?> FicherosElegidos { get; } = new();

    public List<string?> Filtros { get; } = [];

    public string? ElegirFichero(Window owner, string? filtro)
    {
        Filtros.Add(filtro);
        return FicherosElegidos.Count > 0 ? FicherosElegidos.Dequeue() : null;
    }

    public List<(TipoAviso Tipo, string Texto)> Avisos { get; } = [];

    public void Aviso(TipoAviso tipo, string texto) => Avisos.Add((tipo, texto));

    public List<(DependencyObject Origen, object Datos)> Arrastres { get; } = [];

    public void Arrastrar(DependencyObject origen, object datos) => Arrastres.Add((origen, datos));

    /// <summary>Las «pantallas» que se fotografian al buscar un QR.</summary>
    public List<Drawing.Bitmap> Pantallas { get; } = [];

    public IReadOnlyList<Drawing.Bitmap> CapturarPantallas() => [.. Pantallas];

    // Registro -------------------------------------------------------------------

    public Dictionary<string, object> Registro { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Exception? FalloRegistro { get; set; }

    private static string Clave(string clave, string nombre) => $@"{clave}\{nombre}";

    public object? LeerRegistro(string clave, string nombre) =>
        Registro.GetValueOrDefault(Clave(clave, nombre));

    public void EscribirRegistro(string clave, string nombre, string valor)
    {
        if (FalloRegistro is { } fallo)
        {
            throw fallo;
        }

        Registro[Clave(clave, nombre)] = valor;
    }

    public void BorrarRegistro(string clave, string nombre) => Registro.Remove(Clave(clave, nombre));

    // Atajo global -----------------------------------------------------------------

    /// <summary>Si Windows deja coger el atajo o ya lo tiene otra aplicacion.</summary>
    public bool AtajoLibre { get; set; } = true;

    public List<(IntPtr Ventana, uint Modificadores, uint Tecla)> Atajos { get; } = [];

    public int Soltados { get; private set; }

    public bool RegistrarAtajo(IntPtr ventana, int id, uint modificadores, uint tecla)
    {
        Atajos.Add((ventana, modificadores, tecla));
        return AtajoLibre;
    }

    public void SoltarAtajo(IntPtr ventana, int id) => Soltados++;

    // Raton y teclado -----------------------------------------------------------------

    public ModifierKeys Teclas { get; set; }

    public MouseButtonState Boton { get; set; } = MouseButtonState.Released;

    public Point Posicion { get; set; }

    public MouseButtonState BotonIzquierdo(MouseEventArgs e) => Boton;

    public Point PosicionRaton(MouseEventArgs e) => Posicion;

    public bool BandejaVisible => false;
}
