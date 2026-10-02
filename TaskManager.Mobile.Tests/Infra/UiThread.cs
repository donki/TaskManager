using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace TaskManager.Mobile.Tests.Infra;

/// <summary>
/// Un «hilo de interfaz» para las pruebas: un solo hilo con su cola, como el de la aplicacion.
/// </summary>
/// <remarks>
/// <para>Los manejadores de las paginas son <c>async void</c>: quien los llama no tiene una tarea
/// que esperar. Este contexto cuenta las operaciones <c>async void</c> en marcha
/// (<see cref="OperationStarted"/>/<see cref="OperationCompleted"/>), y <see cref="IdleAsync"/>
/// espera a que no quede ninguna. Asi una prueba pulsa un boton, espera a que su manejador acabe de
/// verdad y comprueba el resultado.</para>
/// <para>Una excepcion que se escape de un <c>async void</c> no se pierde: se guarda y la prueba
/// falla con ella.</para>
/// </remarks>
public sealed class UiThread : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
    private readonly List<Exception> _errors = [];
    private int _operations;
    private Thread? _thread;

    public static UiThread? Current => SynchronizationContext.Current as UiThread;

    /// <summary>Operaciones <c>async void</c> que aun no han terminado.</summary>
    public int Pending => Volatile.Read(ref _operations);

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (Thread.CurrentThread == _thread)
        {
            d(state);
            return;
        }

        using var done = new ManualResetEventSlim();
        Exception? error = null;
        Post(_ =>
        {
            try
            {
                d(state);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                done.Set();
            }
        }, null);
        done.Wait();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    public override void OperationStarted() => Interlocked.Increment(ref _operations);

    public override void OperationCompleted()
    {
        Interlocked.Decrement(ref _operations);
        _queue.Add((_ => { }, null));   // despierta la cola por si alguien espera
    }

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>Ejecuta la prueba en este hilo y bombea la cola hasta que acaba.</summary>
    public static void Run(Func<Task> test, int timeoutSeconds = 60)
    {
        var context = new UiThread();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        context._thread = Thread.CurrentThread;
        try
        {
            Task task;
            try
            {
                task = test();
            }
            catch (Exception ex)
            {
                task = Task.FromException(ex);
            }

            task.ContinueWith(_ => context._queue.Add((_ => { }, null)), TaskScheduler.Default);

            var limit = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (!task.IsCompleted)
            {
                if (DateTime.UtcNow > limit)
                {
                    throw new TimeoutException("La prueba no termino a tiempo (¿un dialogo sin respuesta?).");
                }

                if (context._queue.TryTake(out var item, 50))
                {
                    context.Execute(item);
                }
            }

            // Lo que quede en la cola (continuaciones sueltas) tambien se ejecuta.
            while (context._queue.TryTake(out var item))
            {
                context.Execute(item);
            }

            task.GetAwaiter().GetResult();
            context.ThrowErrors();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private void Execute((SendOrPostCallback Callback, object? State) item)
    {
        try
        {
            item.Callback(item.State);
        }
        catch (Exception ex)
        {
            // Lo que se escapa de un async void llega aqui: se guarda para que la prueba falle.
            lock (_errors)
            {
                _errors.Add(ex);
            }
        }
    }

    private void ThrowErrors()
    {
        lock (_errors)
        {
            if (_errors.Count > 0)
            {
                var errors = _errors.ToList();
                _errors.Clear();
                throw new AggregateException("Excepcion sin recoger en un manejador de la interfaz.", errors);
            }
        }
    }

    /// <summary>
    /// Espera a que terminen los manejadores <c>async void</c> en marcha (y lo que hayan dejado en
    /// la cola). Falla si alguno lanzo una excepcion.
    /// </summary>
    public static async Task IdleAsync(int timeoutSeconds = 30)
    {
        var context = Current ?? throw new InvalidOperationException("Fuera del hilo de interfaz de las pruebas.");
        var limit = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var quiet = 0;
        while (quiet < 3)
        {
            if (DateTime.UtcNow > limit)
            {
                throw new TimeoutException($"Quedan {context.Pending} manejadores sin terminar (¿un dialogo sin respuesta?).");
            }

            await Task.Delay(2);
            quiet = context.Pending == 0 && context._queue.Count == 0 ? quiet + 1 : 0;
        }

        context.ThrowErrors();
    }
}
