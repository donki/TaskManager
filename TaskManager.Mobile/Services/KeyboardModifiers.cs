namespace TaskManager.Mobile.Services;

/// <summary>
/// Si la tecla Ctrl esta pulsada ahora mismo, para Ctrl+clic en las pastillas de etiquetas.
/// </summary>
/// <remarks>
/// <para>El <c>Clicked</c> de un boton de MAUI no dice que teclas iban pulsadas, asi que el estado
/// se apunta aqui desde la actividad de Android (<c>MainActivity.DispatchKeyEvent</c>), que ve
/// pasar todas las teclas de un teclado fisico antes que nadie. Sin teclado no se pulsa nunca y
/// las pastillas se comportan como siempre: una sola etiqueta.</para>
///
/// <para>Es un campo suelto y no un servicio porque lo escribe la plataforma y lo leen las paginas,
/// y las pruebas lo ponen a mano para simular Ctrl+clic.</para>
/// </remarks>
public static class KeyboardModifiers
{
    public static bool Ctrl { get; set; }
}
