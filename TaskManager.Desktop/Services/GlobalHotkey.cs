using System.Windows.Interop;

namespace TaskManager.Desktop.Services;

/// <summary>
/// Atajo de teclado global (especificacion 6.B). <c>RegisterHotKey</c> es lo que hace que funcione
/// tambien sobre un juego a pantalla completa: el sistema entrega la pulsacion aunque la aplicacion
/// no tenga el foco.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xA71;

    [Flags]
    private enum Modifiers : uint
    {
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
        NoRepeat = 0x4000,
    }

    private readonly HwndSource _source;
    private bool _registered;

    /// <summary>
    /// Necesita una ventana con handle. Se le pasa la del flyout, que existe desde el arranque
    /// aunque este oculta.
    /// </summary>
    public GlobalHotkey(IntPtr windowHandle)
    {
        _source = HwndSource.FromHwnd(windowHandle)
                  ?? throw new InvalidOperationException("La ventana todavia no tiene handle.");
        _source.AddHook(Hook);
    }

    public event EventHandler? Pressed;

    /// <summary>
    /// Registra la combinacion. Formato: "Ctrl+Alt+T". Devuelve false si otra aplicacion ya la
    /// tiene cogida — hay que decirselo al usuario, no fallar en silencio.
    /// </summary>
    public bool Register(string combination)
    {
        Unregister();

        var (modifiers, key) = Parse(combination);
        if (key == 0)
        {
            return false;
        }

        _registered = Sistema.Actual.RegistrarAtajo(_source.Handle, HotkeyId, modifiers | (uint)Modifiers.NoRepeat, key);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            Sistema.Actual.SoltarAtajo(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    internal static (uint Modifiers, uint Key) Parse(string combination)
    {
        Modifiers modifiers = 0;
        uint key = 0;

        foreach (var raw in combination.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Modifiers.Control; break;
                case "alt": modifiers |= Modifiers.Alt; break;
                case "shift": modifiers |= Modifiers.Shift; break;
                case "win": modifiers |= Modifiers.Win; break;
                default:
                    var parsed = System.Windows.Input.KeyInterop.VirtualKeyFromKey(
                        Enum.TryParse<System.Windows.Input.Key>(raw, true, out var k)
                            ? k
                            : System.Windows.Input.Key.None);
                    key = (uint)parsed;
                    break;
            }
        }

        return ((uint)modifiers, key);
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(Hook);
    }
}
