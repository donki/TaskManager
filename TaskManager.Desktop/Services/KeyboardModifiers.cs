using System.Windows.Input;

namespace TaskManager.Desktop.Services;

/// <summary>
/// Si la tecla Ctrl esta pulsada, para Ctrl+clic en las pastillas de etiquetas (marcar varias).
/// </summary>
/// <remarks>
/// Se pregunta a traves de aqui y no a <c>Keyboard.Modifiers</c> directamente para que las pruebas
/// puedan simular Ctrl+clic sin mandar teclas de verdad al escritorio.
/// </remarks>
public static class KeyboardModifiers
{
    public static Func<bool> Ctrl { get; set; } = () => (Keyboard.Modifiers & ModifierKeys.Control) != 0;
}
