using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskManager.Desktop.Services;

namespace TaskManager.Desktop.Controls;

/// <summary>
/// Lo comun a todas las listas que se ordenan arrastrando y a los dobles clics sobre una fila.
/// </summary>
/// <remarks>
/// Estaba copiado en cada ventana —cinco veces el mismo bucle para subir hasta la fila, tres el
/// mismo umbral de arrastre—, y un arreglo en una copia no llegaba a las otras.
/// </remarks>
internal static class Arrastre
{
    /// <summary>La fila sobre la que esta el raton, subiendo desde lo que se pulso.</summary>
    public static ListBoxItem? Fila(DependencyObject? origen)
    {
        while (origen is not null and not ListBoxItem)
        {
            origen = VisualTreeHelper.GetParent(origen);
        }

        return origen as ListBoxItem;
    }

    /// <summary>Lo que pinta la fila sobre la que esta el raton, si es de ese tipo.</summary>
    public static T? Contenido<T>(DependencyObject? origen) where T : class => Fila(origen)?.Content as T;

    /// <summary>Donde esta el raton, para medir desde ahi cuanto se ha movido.</summary>
    public static Point Posicion(MouseEventArgs e) => Sistema.Actual.PosicionRaton(e);

    /// <summary>
    /// Si el gesto ya es un arrastre: boton pulsado y el raton movido lo que Windows considera un
    /// arrastre de verdad (<see cref="SystemParameters.MinimumHorizontalDragDistance"/>).
    /// </summary>
    /// <remarks>
    /// Sin el umbral, el temblor de la mano al marcar una casilla o abrir el detalle se convertiria
    /// en un arrastre accidental. Con <paramref name="sinTeclas"/>, Ctrl o Mayus pulsados no
    /// arrastran: son los gestos de marcar varias.
    /// </remarks>
    public static bool Empieza(MouseEventArgs e, Point inicio, bool sinTeclas = false)
    {
        if (Sistema.Actual.BotonIzquierdo(e) != MouseButtonState.Pressed)
        {
            return false;
        }

        if (sinTeclas && Sistema.Actual.Teclas != ModifierKeys.None)
        {
            return false;
        }

        var movido = Posicion(e) - inicio;
        return Math.Abs(movido.X) >= SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(movido.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    /// <summary>Al pasar por encima: solo se acepta lo que es una fila de esta lista.</summary>
    public static void AlPasar<T>(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(T)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// De donde a donde se mueve la fila soltada. Soltar fuera de cualquier fila la deja al final,
    /// que es lo que se espera al arrastrar hacia el hueco de abajo. <c>null</c> si no hay nada que
    /// mover.
    /// </summary>
    public static (int Desde, int Hasta)? Destino<T>(IList<T> filas, T movida, T? sobre) where T : class
    {
        var desde = filas.IndexOf(movida);
        var hasta = sobre is null ? filas.Count - 1 : filas.IndexOf(sobre);

        return desde < 0 || hasta < 0 || desde == hasta ? null : (desde, hasta);
    }
}
