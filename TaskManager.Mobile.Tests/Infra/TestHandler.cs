using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Animations;

namespace TaskManager.Mobile.Tests.Infra;

/// <summary>
/// Un «handler» de mentira para las paginas: no pinta nada, pero da a la pagina un contexto de
/// MAUI con su gestor de animaciones. Sin el, <c>FadeToAsync</c> y compania fallan fuera del movil
/// («Unable to find IAnimationManager»).
/// </summary>
public sealed class TestHandler : IViewHandler
{
    public object PlatformView { get; } = new();

    public IView? VirtualView { get; private set; }

    IElement? IElementHandler.VirtualView => VirtualView;

    /// <summary>Uno por pagina: su reloj de animacion late en el hilo de la prueba que la creo.</summary>
    public IMauiContext? MauiContext { get; private set; } = new TestMauiContext();

    public bool HasContainer { get; set; }

    public object? ContainerView => null;

    public void SetMauiContext(IMauiContext mauiContext) => MauiContext = mauiContext;

    public void SetVirtualView(IElement view) => VirtualView = view as IView;

    public void UpdateValue(string property)
    {
    }

    public void Invoke(string command, object? args = null)
    {
    }

    public void DisconnectHandler() => VirtualView = null;

    public Size GetDesiredSize(double widthConstraint, double heightConstraint) => Size.Zero;

    public void PlatformArrange(Rect frame)
    {
    }

    /// <summary>Le pone el handler a la pagina si aun no tiene.</summary>
    public static void Attach(VisualElement element)
    {
        if (element.Handler is null)
        {
            element.Handler = new TestHandler();
        }
    }

    private sealed class TestMauiContext : IMauiContext
    {
        public IServiceProvider Services { get; } = new ServiceCollection()
            .AddSingleton<IAnimationManager>(new AnimationManager(new TestTicker()))
            .BuildServiceProvider();

        public IMauiHandlersFactory Handlers => throw new NotSupportedException();
    }

    /// <summary>Un reloj de animacion que late en el hilo de la prueba (cada ~16 ms).</summary>
    private sealed class TestTicker : ITicker
    {
        private int _generation;

        public bool IsRunning { get; private set; }

        public bool SystemEnabled => true;

        public int MaxFps { get; set; } = 60;

        public Action? Fire { get; set; }

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            IsRunning = true;
            _ = LoopAsync(++_generation);
        }

        public void Stop()
        {
            IsRunning = false;
            _generation++;
        }

        private async Task LoopAsync(int generation)
        {
            while (IsRunning && generation == _generation)
            {
                await Task.Delay(16);
                if (IsRunning && generation == _generation)
                {
                    Fire?.Invoke();
                }
            }
        }
    }
}
