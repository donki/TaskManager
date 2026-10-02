using Microsoft.Maui.Dispatching;

namespace TaskManager.Mobile.Tests.Infra;

/// <summary>
/// El <see cref="IDispatcher"/> de MAUI fuera del movil: lo que se le manda va a la cola del
/// <see cref="UiThread"/> de la prueba. Los temporizadores no corren solos: la prueba los hace
/// avanzar (<see cref="TestTimer.Fire"/>), que es lo que hace deterministas las animaciones.
/// </summary>
public sealed class TestDispatcher : IDispatcher
{
    public static readonly TestDispatcher Instance = new();

    public List<TestTimer> Timers { get; } = [];

    public bool IsDispatchRequired => false;

    public bool Dispatch(Action action)
    {
        Post(action);
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        _ = DelayThenAsync(delay, action);
        return true;
    }

    private static async Task DelayThenAsync(TimeSpan delay, Action action)
    {
        await Task.Delay(delay);
        Post(action);
    }

    private static void Post(Action action)
    {
        if (SynchronizationContext.Current is { } context)
        {
            context.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }

    public IDispatcherTimer CreateTimer()
    {
        var timer = new TestTimer();
        lock (Timers)
        {
            Timers.Add(timer);
        }

        return timer;
    }
}

public sealed class TestTimer : IDispatcherTimer
{
    public TimeSpan Interval { get; set; }

    public bool IsRepeating { get; set; } = true;

    public bool IsRunning { get; private set; }

    public event EventHandler? Tick;

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    /// <summary>Un tic, como si hubiera pasado <see cref="Interval"/>.</summary>
    public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
}

public sealed class TestDispatcherProvider : IDispatcherProvider
{
    public IDispatcher? GetForCurrentThread() => TestDispatcher.Instance;
}
