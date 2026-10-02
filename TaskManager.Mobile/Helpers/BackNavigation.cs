namespace TaskManager.Mobile.Helpers;

/// <summary>
/// Una pantalla con algo abierto encima (modo seleccion, buscador con texto, cambios sin guardar)
/// que quiere cerrarlo con el boton de atras antes de que el Shell vuelva atras (Mobile 7).
/// </summary>
public interface IBackHandler
{
    /// <summary>
    /// <c>true</c> si la pantalla se ha quedado el boton (ha cerrado algo o va a preguntar); con
    /// <c>false</c>, el Shell sigue con lo suyo: volver a la pantalla anterior o a Inicio.
    /// </summary>
    bool HandleBack();
}

/// <summary>Lo que hace el boton de atras cuando no hay menu lateral abierto.</summary>
public enum BackAction
{
    /// <summary>Lo ha atendido la pantalla (o un dialogo): no se navega.</summary>
    Handled,

    /// <summary>Hay una pantalla apilada: se vuelve a la anterior.</summary>
    Pop,

    /// <summary>Otra pantalla del menu: se vuelve a «Mis tareas».</summary>
    GoHome,

    /// <summary>En «Mis tareas» o en la entrada: se oculta la aplicacion.</summary>
    Hide,
}

/// <summary>
/// Lo que el boton de atras cierra antes de navegar.
/// </summary>
public static class BackNavigation
{
    /// <summary>La pantalla de inicio: atras desde otra del menu vuelve aqui.</summary>
    public const string HomeRoute = "MyTasksPage";

    /// <summary>
    /// Que hace atras (Mobile 7): primero se cierra lo que haya encima —un dialogo, o lo que la
    /// propia pantalla tenga abierto—; despues, con una pantalla apilada se vuelve a la anterior;
    /// desde otra pantalla del menu se vuelve a «Mis tareas», y en «Mis tareas» (o en la entrada,
    /// si no hay sesion) la aplicacion se oculta.
    /// </summary>
    public static BackAction Decide(Page? page, int stackCount, string? route)
    {
        if (CloseDialog(page))
        {
            return BackAction.Handled;
        }

        if (page is IBackHandler handler && handler.HandleBack())
        {
            return BackAction.Handled;
        }

        if (stackCount > 1)
        {
            return BackAction.Pop;
        }

        return route is not (HomeRoute or "LoginPage") ? BackAction.GoHome : BackAction.Hide;
    }

    // El mismo identificador con el que SocShared.ModernDialog marca su velo.
    private const string DialogOverlayId = "__modernDialogOverlay";

    /// <summary>
    /// Cierra el dialogo de <c>ModernDialog</c> que haya a la vista, como si se tocara fuera de la
    /// tarjeta. Es un velo dentro de la pagina, no una ventana del sistema, asi que Android no sabe
    /// que esta ahi: sin esto, atras cambiaba de pantalla con el dialogo abierto y quien lo esperaba
    /// se quedaba colgado.
    /// </summary>
    public static bool CloseDialog(Page? page)
    {
        if (page is not ContentPage { Content: Grid host })
        {
            return false;
        }

        var overlay = host.Children.OfType<Grid>().LastOrDefault(g => g.StyleId == DialogOverlayId);
        if (overlay is null)
        {
            return false;
        }

        // Tocar el velo es la salida de «cancelar» del propio dialogo: completa su tarea con la
        // respuesta de descartar y lo quita con su animacion.
        var scrim = overlay.Children.OfType<BoxView>().FirstOrDefault();
        var tap = scrim?.GestureRecognizers.OfType<TapGestureRecognizer>().FirstOrDefault();
        if (scrim is null || tap is null || SendTapped is null)
        {
            host.Remove(overlay);
            return true;
        }

        SendTapped.Invoke(tap, [scrim, null]);
        return true;
    }

    // TapGestureRecognizer.SendTapped es interno en MAUI 10: es lo que llama el propio control al
    // tocarlo, y la unica forma de lanzar su evento desde fuera.
    private static readonly System.Reflection.MethodInfo? SendTapped = typeof(TapGestureRecognizer).GetMethod(
        "SendTapped", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
}
