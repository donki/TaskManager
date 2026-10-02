using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TaskManager.Desktop.Tests.Banco;

/// <summary>Respuestas tipicas a los dialogos (ModernDialog y Prompt): lo que haria el usuario.</summary>
internal static class Dialogo
{
    /// <summary>Lo que dice el dialogo, para comprobarlo despues.</summary>
    public static List<List<string>> Leidos { get; } = [];

    private static void Leer(Window w) => Leidos.Add(Ui.Textos(w));

    private static void Clic(ButtonBase boton) =>
        boton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, boton));

    /// <summary>El boton de aceptar: el ultimo de la fila.</summary>
    public static void Aceptar(Window w)
    {
        Leer(w);
        Clic(Ui.Botones(w).Last());
    }

    /// <summary>El primero: cancelar en las preguntas, la salida alternativa al elegir.</summary>
    public static void Primero(Window w)
    {
        Leer(w);
        Clic(Ui.Botones(w).First());
    }

    /// <summary>Cierra sin pulsar nada (la X o Escape).</summary>
    public static void Cerrar(Window w)
    {
        Leer(w);
        w.Close();
    }

    /// <summary>Elige la opcion n de la lista y acepta.</summary>
    public static Action<Window> Elegir(int indice) => w =>
    {
        Leer(w);
        Ui.Buscar<ListBox>(w).First().SelectedIndex = indice;
        Clic(Ui.Botones(w).Last());
    };

    /// <summary>Escribe en la caja y acepta.</summary>
    public static Action<Window> Escribir(string texto) => w =>
    {
        Leer(w);
        Ui.Buscar<TextBox>(w).First().Text = texto;
        Clic(Ui.Botones(w).Last());
    };
}
