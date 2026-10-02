using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TaskManager.Core.Services;
using TaskManager.Desktop.Localization;
using TaskManager.Desktop.Services;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TaskManager.Desktop.Tests.Banco;

/// <summary>
/// El hilo de la interfaz de las pruebas: un hilo STA con su <see cref="Dispatcher"/> y la
/// <see cref="Desktop.App"/> de verdad (con los recursos de App.xaml y el tema de HandyControl).
/// </summary>
/// <remarks>
/// <para>WPF solo admite una <see cref="Application"/> por proceso, asi que hay una para todo el
/// banco, y cada prueba corre entera en ese hilo (<see cref="Run"/>). Las pruebas no corren en
/// paralelo: comparten el sistema de mentira y la cola de respuestas a los dialogos.</para>
///
/// <para>Ninguna ventana sale en la pantalla: <see cref="Ventanas.Abriendo"/> las lleva a
/// (-32000, -32000) sin activarlas, y los dialogos modales los contesta la prueba
/// (<see cref="Responder(Action{Window}[])"/>).</para>
/// </remarks>
internal static class Ui
{
    private static readonly Dispatcher Hilo;
    private static readonly Queue<Func<Window, Task>> Respuestas = new();

    static Ui()
    {
        Dispatcher? hilo = null;
        var listo = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            hilo = Dispatcher.CurrentDispatcher;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(hilo));

                App = new App();
                App.InitializeComponent();

                Ventanas.Abriendo += AlAbrir;
                Ventanas.Apagar = () => Apagados++;
                Ventanas.Activar = _ => { };

                hilo.UnhandledException += (_, e) =>
                {
                    Errores.Add(e.Exception);
                    e.Handled = true;
                };
            }
            catch (Exception ex)
            {
                FalloAlArrancar = ex;
            }

            listo.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Pruebas: interfaz",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        listo.Wait(TimeSpan.FromSeconds(60));
        Hilo = hilo!;
    }

    private static Exception? FalloAlArrancar;

    public static App App { get; private set; } = null!;

    public static SistemaFalso Sistema { get; private set; } = new();

    /// <summary>Cuantas veces se ha pedido apagar la aplicacion.</summary>
    public static int Apagados { get; private set; }

    /// <summary>Excepciones que se han escapado al hilo de la interfaz. Una prueba acaba con ninguna.</summary>
    public static List<Exception> Errores { get; } = [];

    /// <summary>Todas las ventanas que se han abierto en la prueba, modales o no.</summary>
    public static List<Window> Abiertas { get; } = [];

    /// <summary>Los textos de los dialogos que se han abierto sin que la prueba los esperara.</summary>
    public static List<string> Inesperados { get; } = [];

    /// <summary>Carpeta de datos de la prueba (sustituye a %LOCALAPPDATA%\Socratic\TaskManager).</summary>
    public static string Carpeta { get; private set; } = string.Empty;

    /// <summary>Corre la prueba en el hilo de la interfaz, con todo lo de la anterior limpio.</summary>
    public static Task Run(Func<Task> prueba)
    {
        if (FalloAlArrancar is { } fallo)
        {
            return Task.FromException(new InvalidOperationException("No arranca el hilo de la interfaz", fallo));
        }

        var resultado = new TaskCompletionSource();

        Hilo.BeginInvoke(async () =>
        {
            try
            {
                Reiniciar();
                await prueba();
                await Calma();

                if (Inesperados.Count > 0)
                {
                    throw new Xunit.Sdk.XunitException("Dialogos sin respuesta: " + string.Join(" || ", Inesperados));
                }
                if (Errores.Count > 0)
                {
                    throw new AggregateException("Errores en el hilo de la interfaz", Errores);
                }

                resultado.SetResult();
            }
            catch (Exception ex)
            {
                resultado.TrySetException(ex);
            }
            finally
            {
                CerrarTodo();
            }
        });

        return resultado.Task.WaitAsync(TimeSpan.FromSeconds(90));
    }

    private static void Reiniciar()
    {
        Sistema = new SistemaFalso();
        Services.Sistema.Actual = Sistema;
        Apagados = 0;
        Errores.Clear();
        Abiertas.Clear();
        Inesperados.Clear();
        Respuestas.Clear();

        Carpeta = Path.Combine(Path.GetTempPath(), "tm-desktop-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Carpeta);
        Rutas.Carpeta = Carpeta;
        Rutas.Instancia = $"Socratic.TaskManager.Pruebas.{Guid.NewGuid():N}.{{0}}";
    }

    private static void CerrarTodo()
    {
        foreach (var ventana in App.Windows.OfType<Window>().ToList())
        {
            if (ventana is FlyoutWindow panel)
            {
                panel.CloseForReal();
            }
            else
            {
                ventana.Close();
            }
        }
    }

    // ---------------------------------------------------------------------------------
    // Ventanas y dialogos
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Lo que se contestara a los proximos dialogos modales, en orden. Cada respuesta tiene que
    /// cerrar su dialogo (pulsando un boton, por ejemplo).
    /// </summary>
    public static void Responder(params Action<Window>[] respuestas)
    {
        foreach (var respuesta in respuestas)
        {
            Respuestas.Enqueue(w =>
            {
                respuesta(w);
                return Task.CompletedTask;
            });
        }
    }

    /// <summary>Igual, pero la respuesta puede esperar (a que cargue la ventana, por ejemplo).</summary>
    public static void ResponderAsync(Func<Window, Task> respuesta) => Respuestas.Enqueue(respuesta);

    public static int RespuestasPendientes => Respuestas.Count;

    private static void AlAbrir(Window ventana, bool modal)
    {
        ventana.WindowStartupLocation = WindowStartupLocation.Manual;
        ventana.Left = -32000;
        ventana.Top = -32000;
        ventana.ShowActivated = false;
        ventana.ShowInTaskbar = false;
        Abiertas.Add(ventana);

        if (!modal)
        {
            return;
        }

        var respuesta = Respuestas.Count > 0 ? Respuestas.Dequeue() : null;

        ventana.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, async () =>
        {
            try
            {
                if (respuesta is null)
                {
                    Inesperados.Add(string.Join(" | ", Textos(ventana)));
                    ventana.Close();
                    return;
                }

                await respuesta(ventana);
            }
            catch (Exception ex)
            {
                Errores.Add(ex);
                ventana.Close();
            }
        });
    }

    /// <summary>Enseña una ventana como lo haria la aplicacion, pero fuera de la pantalla.</summary>
    public static async Task<T> Mostrar<T>(T ventana) where T : Window
    {
        Ventanas.Mostrar(ventana);
        await Calma();
        return ventana;
    }

    // ---------------------------------------------------------------------------------
    // Esperas
    // ---------------------------------------------------------------------------------

    /// <summary>Espera a que se cumpla algo, dejando correr la interfaz mientras tanto.</summary>
    public static async Task Hasta(Func<bool> condicion, int ms = 10000, string? que = null)
    {
        var limite = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condicion())
        {
            if (DateTime.UtcNow > limite)
            {
                throw new TimeoutException($"No se ha cumplido: {que ?? "la condicion"}");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>Deja que la interfaz termine lo que tenga en cola (cargas, pintado).</summary>
    public static async Task Calma(int vueltas = 3)
    {
        for (var i = 0; i < vueltas; i++)
        {
            await Task.Delay(15);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        }
    }

    /// <summary>
    /// Hace algo que dispara manejadores <c>async void</c> y espera a que terminen todos.
    /// </summary>
    /// <remarks>
    /// Un <c>async void</c> avisa a su contexto de sincronizacion al empezar y al acabar; se le pone
    /// uno que los cuenta, y se espera a que la cuenta vuelva a cero.
    /// </remarks>
    public static async Task Hacer(Action accion, int ms = 15000)
    {
        var contador = new Contador(Hilo);
        var antes = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(contador);
        try
        {
            accion();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(antes);
        }

        await Hasta(() => contador.Pendientes == 0, ms, "que terminen los manejadores");
        await Calma();
    }

    private sealed class Contador(Dispatcher hilo) : SynchronizationContext
    {
        private int _pendientes;

        public int Pendientes => Volatile.Read(ref _pendientes);

        public override void OperationStarted() => Interlocked.Increment(ref _pendientes);

        public override void OperationCompleted() => Interlocked.Decrement(ref _pendientes);

        public override void Post(SendOrPostCallback d, object? state) => hilo.BeginInvoke(d, state);

        public override void Send(SendOrPostCallback d, object? state) => hilo.Invoke(d, state);

        public override SynchronizationContext CreateCopy() => this;
    }

    // ---------------------------------------------------------------------------------
    // Gestos
    // ---------------------------------------------------------------------------------

    /// <summary>Pulsa un boton como lo haria el raton.</summary>
    public static Task Pulsar(ButtonBase boton) =>
        Hacer(() => boton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, boton)));

    /// <summary>Marca o desmarca una casilla o pastilla, con sus eventos.</summary>
    public static Task Marcar(ToggleButton boton, bool marcado) => Hacer(() => boton.IsChecked = marcado);

    /// <summary>Escribe en una caja de texto (dispara TextChanged).</summary>
    public static Task Escribir(TextBox caja, string texto) => Hacer(() => caja.Text = texto);

    /// <summary>Pulsa una tecla sobre un elemento.</summary>
    public static Task Tecla(UIElement elemento, Key tecla, RoutedEvent? evento = null) => Hacer(() =>
    {
        var fuente = PresentationSource.FromVisual((Visual)elemento) ?? new HwndFalso();
        elemento.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, fuente, 0, tecla)
        {
            RoutedEvent = evento ?? Keyboard.KeyDownEvent,
        });
    });

    /// <summary>Llama a un manejador privado de la ventana, como si lo hubiera disparado el XAML.</summary>
    public static Task Llamar(object objetivo, string metodo, params object?[] argumentos) =>
        Hacer(() => Invocar(objetivo, metodo, argumentos));

    /// <summary>Llama a un metodo privado y devuelve lo que devuelva.</summary>
    public static object? Invocar(object objetivo, string metodo, params object?[] argumentos)
    {
        var tipo = objetivo as Type ?? objetivo.GetType();
        var m = tipo.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(x => x.Name == metodo && x.GetParameters().Length == argumentos.Length);
        try
        {
            return m.Invoke(objetivo is Type ? null : objetivo, argumentos);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Lee un campo privado.</summary>
    public static T Campo<T>(object objetivo, string nombre) =>
        (T)objetivo.GetType().GetField(nombre, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(objetivo)!;

    /// <summary>Un evento de raton con el origen puesto, para los manejadores que suben hasta la fila.</summary>
    public static MouseButtonEventArgs Raton(DependencyObject origen, RoutedEvent? evento = null)
    {
        var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = evento ?? Control.MouseDoubleClickEvent,
        };
        e.Source = origen;
        return e;
    }

    /// <summary>Un evento de soltar lo arrastrado, con lo que se arrastra y donde cae.</summary>
    public static DragEventArgs Soltar(object datos, DependencyObject sobre)
    {
        var data = new DataObject(datos.GetType(), datos);
        var ctor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var e = (DragEventArgs)ctor.Invoke([data, DragDropKeyStates.None, DragDropEffects.Move, sobre, new Point(0, 0)]);
        e.RoutedEvent = DragDrop.DropEvent;
        e.Source = sobre;
        return e;
    }

    // ---------------------------------------------------------------------------------
    // Arbol visual
    // ---------------------------------------------------------------------------------

    /// <summary>Todo lo que cuelga de un elemento, por el arbol visual y por el logico.</summary>
    public static IEnumerable<DependencyObject> Todo(DependencyObject raiz)
    {
        var vistos = new HashSet<DependencyObject>();
        var pendientes = new Stack<DependencyObject>();
        pendientes.Push(raiz);

        while (pendientes.Count > 0)
        {
            var actual = pendientes.Pop();
            if (!vistos.Add(actual))
            {
                continue;
            }

            yield return actual;

            if (actual is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(actual); i++)
                {
                    pendientes.Push(VisualTreeHelper.GetChild(actual, i));
                }
            }

            foreach (var hijo in LogicalTreeHelper.GetChildren(actual).OfType<DependencyObject>())
            {
                pendientes.Push(hijo);
            }
        }
    }

    public static IEnumerable<T> Buscar<T>(DependencyObject raiz) => Todo(raiz).OfType<T>();

    /// <summary>Los textos visibles de una ventana, para saber que dice un dialogo.</summary>
    public static List<string> Textos(DependencyObject raiz) =>
        [.. Buscar<TextBlock>(raiz).Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t))];

    /// <summary>Los botones de un dialogo, en el orden en que se pintan.</summary>
    public static List<Button> Botones(DependencyObject raiz)
    {
        var lista = new List<Button>();
        void Recorrer(DependencyObject nodo)
        {
            if (nodo is Button b)
            {
                lista.Add(b);
            }

            foreach (var hijo in LogicalTreeHelper.GetChildren(nodo).OfType<DependencyObject>())
            {
                Recorrer(hijo);
            }
        }

        Recorrer(raiz);
        return lista;
    }

    /// <summary>La fila (ListBoxItem) que pinta ese elemento, ya generada.</summary>
    public static async Task<ListBoxItem> Fila(ItemsControl lista, object elemento)
    {
        // En una pestaña que no se ve no se generan las filas: se elige la suya.
        for (DependencyObject? padre = lista; padre is not null; padre = LogicalTreeHelper.GetParent(padre))
        {
            if (padre is TabItem pestaña)
            {
                pestaña.IsSelected = true;
            }
        }

        await Calma();
        lista.UpdateLayout();
        await Hasta(() => lista.ItemContainerGenerator.ContainerFromItem(elemento) is ListBoxItem, 5000, "la fila");
        return (ListBoxItem)lista.ItemContainerGenerator.ContainerFromItem(elemento);
    }

    /// <summary>Algo de dentro de la fila: el primer TextBlock de su plantilla.</summary>
    public static DependencyObject DentroDe(ListBoxItem fila)
    {
        fila.ApplyTemplate();
        fila.UpdateLayout();
        return Buscar<TextBlock>(fila).FirstOrDefault() as DependencyObject ?? fila;
    }

    private sealed class HwndFalso : PresentationSource
    {
        public override Visual? RootVisual { get; set; }

        public override bool IsDisposed => false;

        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }

    // ---------------------------------------------------------------------------------
    // Datos
    // ---------------------------------------------------------------------------------

    /// <summary>Una base temporal con la cuenta dentro y los textos en ese idioma.</summary>
    public static async Task<Datos> Datos(string idioma = "es", string cuenta = "cuenta-a")
    {
        var store = await TaskManager.Tests.TestStore.CreateAsync(cuenta, idioma);
        Loc.Use(store.Texts);
        return new Datos(store);
    }
}

/// <summary>Una base de usar y tirar con sus servicios, como los tiene la aplicacion.</summary>
internal sealed class Datos(TaskManager.Tests.TestStore store)
{
    public TaskManager.Tests.TestStore Store { get; } = store;

    public SettingsService Settings => Store.Settings;

    public TaskManager.Core.Data.TaskRepository Repo => Store.Repository;

    public TaskService Tasks { get; } = store.NewService();
}
