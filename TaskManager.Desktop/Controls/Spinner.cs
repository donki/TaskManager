using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TaskManager.Desktop.Controls;

/// <summary>
/// Hace girar un icono mientras se espera a algo.
/// </summary>
/// <remarks>
/// <para><b>Por que gira el propio icono y no aparece una rueda aparte.</b> El boton de actualizar
/// habla con el servidor y espera a que termine; hasta ahora lo unico que pasaba era que se apagaba,
/// y con la red lenta parecia que no hacia nada. Girar el icono que ya esta ahi da la respuesta en
/// el sitio donde se ha pulsado y no mueve ni un pixel de la maqueta, que en el panel rapido
/// —que mide lo justo— importa.</para>
///
/// <para>Los botones de actualizar son de fondo transparente (<c>GhostIconButton</c>), asi que girar
/// el boton entero es exactamente girar su glifo.</para>
/// </remarks>
public static class Spinner
{
    private static readonly Duration Vuelta = new(TimeSpan.FromSeconds(1));

    /// <summary>Empieza a girar. Repetirlo sobre algo que ya gira no hace nada.</summary>
    public static void Start(params FrameworkElement[] elements)
    {
        foreach (var element in elements)
        {
            var rotation = RotationOf(element);
            rotation.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, Vuelta) { RepeatBehavior = RepeatBehavior.Forever });
        }
    }

    /// <summary>Para y deja el icono derecho, no en el angulo donde le pillara el final.</summary>
    public static void Stop(params FrameworkElement[] elements)
    {
        foreach (var element in elements)
        {
            var rotation = RotationOf(element);
            rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            rotation.Angle = 0;
        }
    }

    /// <summary>
    /// El giro del elemento, poniendoselo la primera vez. Se centra
    /// (<see cref="UIElement.RenderTransformOrigin"/>) porque por defecto se giraria desde la
    /// esquina y el icono se iria de paseo en vez de dar vueltas sobre si mismo.
    /// </summary>
    private static RotateTransform RotationOf(FrameworkElement element)
    {
        if (element.RenderTransform is RotateTransform existing)
        {
            return existing;
        }

        var rotation = new RotateTransform();
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = rotation;
        return rotation;
    }
}
