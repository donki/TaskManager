using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using TaskManager.Core.Services;
using TaskManager.Desktop.Localization;
using TaskManager.Desktop.Services;
using TaskManager.Desktop.Tests.Banco;
using TaskManager.Tests;
using WinForms = System.Windows.Forms;

namespace TaskManager.Desktop.Tests;

/// <summary>
/// El arranque entero de la aplicacion, con la base en una carpeta temporal, el servidor de mentira
/// y la instancia unica con un nombre propio de las pruebas: nunca el de la aplicacion de verdad.
/// </summary>
public class AppTests
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private static App App => Ui.App;

    private static void Salir() => Ui.Invocar(App, "OnExit", [null]);

    /// <summary>Arranca la aplicacion; la puerta se pasa entrando sin cuenta.</summary>
    private static async Task ArrancarAsync(Action<Window>[] despues, params string[] args)
    {
        // La base que se encontrara la aplicacion, en castellano (si no, seguiria al idioma del equipo).
        var db = new TaskManager.Core.Data.LocalDatabase(Path.Combine(Ui.Carpeta, "taskmanager.db3"));
        var ajustes = new SettingsService(db);
        await ajustes.LoadAsync();
        await ajustes.SetAsync(SettingsService.KeyLanguage, "es");
        await db.Connection.CloseAsync();

        App.CrearHttp = () => new FakeHttp().Throw(HttpMethod.Post, string.Empty).Throw(HttpMethod.Get, string.Empty).Client();
        Ui.ResponderAsync(async w => await Ui.Pulsar(((LoginWindow)w).LocalButton));
        Ui.Responder(despues);
        await App.ArrancarAsync(args).WaitAsync(TimeSpan.FromSeconds(60));
        await Ui.Calma();
        Assert.NotNull(App.Bandeja.LastNotification);
    }

    private static void Disparar<T>(object objetivo, string evento, T argumento)
    {
        var campo = objetivo.GetType().GetField(evento, BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((Delegate?)campo.GetValue(objetivo))?.DynamicInvoke(objetivo, argumento);
    }

    [Fact]
    public Task Arranca_en_la_bandeja_y_cada_boton_abre_su_ventana() => Ui.Run(async () =>
    {
        var invite = new GroupInvite("ENL123", "k");
        Ui.Sistema.AtajoLibre = false;

        // Abierta desde un enlace: se enseña la principal y se pregunta (se dice que no).
        await ArrancarAsync([Dialogo.Primero], "--tray", GroupLink.For(invite).ToString());

        try
        {
            Assert.Equal(0, Ui.Apagados);
            Assert.NotNull(App.Principal);
            Assert.False(File.Exists(GroupLinkProtocol.BuzonPath));
            Assert.Contains(Ui.Abiertas, w => w is WhatsNewWindow);
            Assert.True(File.Exists(Path.Combine(Ui.Carpeta, "taskmanager.db3")));
            Assert.Equal($@"""{Environment.ProcessPath}"" ""%1""",
                Ui.Sistema.Registro[$@"Software\Classes\{GroupLink.Scheme}\shell\open\command\"]);
            Assert.Equal(Loc.Format("TrayRunning", "Ctrl+Alt+T"), App.Bandeja.LastNotification!.Value.Message);
            Assert.Single(Ui.Sistema.Atajos);

            // Clic en el icono: el panel.
            var abierto = false;
            DependencyPropertyChangedEventHandler abre = (_, e) => abierto |= (bool)e.NewValue;
            App.Panel.IsVisibleChanged += abre;
            await Ui.Hacer(() => App.Bandeja.OnMouseClick(WinForms.MouseButtons.Left));
            await Ui.Hasta(() => abierto, que: "el clic en el icono abre el panel");
            App.Panel.IsVisibleChanged -= abre;
            await Ui.Hasta(() => App.Bandeja.Tooltip == Loc.Get("TrayUpToDate"));

            // El atajo global tambien lo abre.
            // (Se mira que llegue a verse, no que siga visible: el panel se esconde al perder el foco,
            // y en un escritorio con otras ventanas eso puede pasar justo despues.)
            App.Panel.HideFlyout();
            var mostrado = false;
            DependencyPropertyChangedEventHandler visto = (_, e) => mostrado |= (bool)e.NewValue;
            App.Panel.IsVisibleChanged += visto;
            await Ui.Hacer(() => SendMessage(new WindowInteropHelper(App.Panel).Handle, 0x0312, (IntPtr)0xA71, IntPtr.Zero));
            await Ui.Hasta(() => mostrado, que: "el atajo global abre el panel");
            App.Panel.IsVisibleChanged -= visto;

            // Menu de la bandeja: principal (ya abierta: al frente), ajustes, novedades (ya abiertas) y salir.
            var menu = App.Bandeja.Menu!.Items.OfType<WinForms.ToolStripMenuItem>().ToList();
            var principal = App.Principal;
            principal!.WindowState = WindowState.Minimized;
            await Ui.Hacer(() => menu[1].PerformClick());
            Assert.Same(principal, App.Principal);
            Assert.Equal(WindowState.Normal, principal.WindowState);

            Ui.Responder(Dialogo.Cerrar);
            await Ui.Hacer(() => menu[2].PerformClick());
            Assert.Contains(Ui.Abiertas, w => w is SettingsWindow);

            var novedades = Ui.Abiertas.Count(w => w is WhatsNewWindow);
            await Ui.Hacer(() => menu[3].PerformClick());
            Assert.Equal(novedades, Ui.Abiertas.Count(w => w is WhatsNewWindow));

            await Ui.Hacer(() => menu[^1].PerformClick());
            Assert.Equal(1, Ui.Apagados);

            // Los botones del panel: calendario (dos veces: la segunda lo trae), principal, ajustes y Acerca de.
            await Ui.Hacer(App.Panel.ShowFlyout);
            await Ui.Pulsar(App.Panel.CalendarButton);
            await Ui.Pulsar(App.Panel.CalendarButton);
            Assert.Single(Ui.Abiertas.OfType<CalendarWindow>());
            await Ui.Pulsar(App.Panel.MainButton);

            Ui.Responder(Dialogo.Cerrar);
            await Ui.Pulsar(App.Panel.SettingsButton);

            // Acerca de: la version, el contacto y las novedades.
            Ui.ResponderAsync(async w =>
            {
                var acerca = (AboutWindow)w;
                Assert.StartsWith("v", acerca.VersionLabel.Text);
                await Ui.Hacer(() => Ui.Invocar(acerca, "OnContactClick", null,
                    new System.Windows.Navigation.RequestNavigateEventArgs(new Uri("mailto:hola@socratic.es"), null)));
                Assert.Equal("mailto:hola@socratic.es", Ui.Sistema.Abiertos.Last());

                // Sin programa de correo se dice.
                Ui.Sistema.FalloAlAbrir = new InvalidOperationException("sin correo");
                Ui.Responder(Dialogo.Aceptar);
                await Ui.Hacer(() => Ui.Invocar(acerca, "OnContactClick", null,
                    new System.Windows.Navigation.RequestNavigateEventArgs(new Uri("mailto:x@y.es"), null)));
                Ui.Sistema.FalloAlAbrir = null;

                // Elegir el idioma que ya esta no cambia nada.
                await Ui.Pulsar(acerca.SpanishButton);
                Assert.True(acerca.IsVisible);

                await Ui.Llamar(acerca, "OnWhatsNewClick", null, new RoutedEventArgs());
            });
            await Ui.Pulsar(App.Panel.AboutButton);
            Assert.False(App.Panel.IsVisible);

            // Una tarea que baja del movil se anuncia, y un cambio repinta el panel y la bandeja.
            var sincronizacion = Ui.Campo<SyncCoordinator>(App, "_syncing");
            Disparar(sincronizacion, "TaskArrived", new ArrivedTask(Guid.NewGuid(), "Del movil"));
            Assert.Equal(Loc.Format("TaskArrivedFromDevice", "Del movil"), App.Bandeja.LastNotification!.Value.Message);

            var repo = Ui.Campo<TaskService>(App, "_tasks").Repository;
            var lista = await repo.GetOrCreateDefaultListAsync("Casa");
            await repo.AddTaskAsync(lista.Id, "Nueva");
            await Ui.Hacer(() => Disparar(sincronizacion, "Changed", EventArgs.Empty));
            await Ui.Hasta(() => App.Bandeja.Pending == 1, que: "el contador de la bandeja");

            // La otra copia pide que se enseñe: se trae la principal.
            App.Principal?.Close();
            Assert.Null(App.Principal);
            EventWaitHandle.OpenExisting(string.Format(Rutas.Instancia, "abrir")).Set();
            await Ui.Hasta(() => App.Principal is not null, que: "la principal desde la otra copia");
        }
        finally
        {
            Salir();
        }
    });

    [Fact]
    public Task Cambiar_de_idioma_y_de_cuenta_rehace_la_interfaz() => Ui.Run(async () =>
    {
        await ArrancarAsync([]);
        try
        {
            Assert.Equal("es", Loc.Language);
            Assert.Single(Ui.Sistema.Atajos);

            // Desde Acerca de, a ingles.
            var panel = App.Panel;
            Ui.ResponderAsync(w => Ui.Pulsar(((AboutWindow)w).EnglishButton));
            await Ui.Pulsar(App.Panel.AboutButton);
            Assert.Equal("en", Loc.Language);
            Assert.NotSame(panel, App.Panel);
            Assert.Equal(Loc.Get("TrayExit"), App.Bandeja.Menu!.Items[^1].Text);
            Assert.Equal(2, Ui.Sistema.Atajos.Count);

            // Desde Ajustes, de vuelta a castellano.
            Ui.ResponderAsync(async w =>
            {
                var ajustes = (SettingsWindow)w;
                ajustes.LanguageBox.SelectedIndex = 1;
                await Ui.Pulsar(ajustes.SaveButton);
            });
            await Ui.Pulsar(App.Panel.SettingsButton);
            Assert.Equal("es", Loc.Language);

            // Salir de la cuenta y entrar otra vez (sin cuenta): se rehace todo con la cuenta nueva.
            await Ui.Pulsar(App.Panel.MainButton);
            await Ui.Pulsar(App.Panel.CalendarButton);
            Assert.NotNull(App.Principal);
            panel = App.Panel;
            Ui.ResponderAsync(async w =>
            {
                var ajustes = (SettingsWindow)w;
                Ui.ResponderAsync(p => Ui.Pulsar(((LoginWindow)p).LocalButton));
                await Ui.Pulsar(ajustes.SignOutButton);
                ajustes.Close();
            });
            await Ui.Pulsar(App.Panel.SettingsButton);
            Assert.Null(App.Principal);
            Assert.NotSame(panel, App.Panel);
            Assert.DoesNotContain(Ui.App.Windows.OfType<CalendarWindow>(), c => c.IsVisible);
            Assert.Equal(0, Ui.Apagados);
        }
        finally
        {
            Salir();
        }
    });

    [Fact]
    public Task Cerrar_la_puerta_sin_entrar_no_monta_nada() => Ui.Run(async () =>
    {
        App.CrearHttp = () => new FakeHttp().Client();
        Ui.Responder(Dialogo.Cerrar);

        // Por la puerta de verdad: OnStartup, con los argumentos del proceso (los del banco).
        var inicio = (StartupEventArgs)typeof(StartupEventArgs)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!.Invoke(null);
        await Ui.Hacer(() => Ui.Invocar(App, "OnStartup", inicio), 60000);
        Assert.Equal(1, Ui.Apagados);
        Assert.Contains(Ui.Abiertas, w => w is LoginWindow);
        Assert.DoesNotContain(Ui.Abiertas, w => w is MainWindow);
    });

    [Fact]
    public Task Una_segunda_copia_deja_la_invitacion_avisa_a_la_primera_y_se_apaga() => Ui.Run(async () =>
    {
        var invite = new GroupInvite("SEG", "k");
        using var primera = new Mutex(true, string.Format(Rutas.Instancia, "unica"));

        // La primera todavia no ha creado el aviso: la segunda se apaga igual.
        await App.ArrancarAsync([GroupLink.For(invite).ToString()]);
        Assert.Equal(1, Ui.Apagados);
        Assert.Equal(invite, GroupLinkProtocol.Recoger());

        // Con el aviso creado, se le avisa.
        using var aviso = new EventWaitHandle(false, EventResetMode.AutoReset, string.Format(Rutas.Instancia, "abrir"));
        await App.ArrancarAsync([]);
        Assert.Equal(2, Ui.Apagados);
        Assert.True(aviso.WaitOne(0));
        Assert.Null(GroupLinkProtocol.Recoger());
        primera.ReleaseMutex();
    });

#if DEBUG
    [Fact]
    public Task La_vista_previa_de_novedades_no_toca_nada() => Ui.Run(async () =>
    {
        Ui.Responder(Dialogo.Cerrar);
        try
        {
            await App.ArrancarAsync(["--preview-novedades", "en"]);
            Assert.Equal("en", Loc.Language);
            Assert.Contains(Ui.Abiertas, w => w is AboutWindow);
            Assert.Contains(Ui.Abiertas, w => w is WhatsNewWindow);
        }
        finally
        {
            // Las pruebas no pueden dejar que cerrar la ultima ventana apague su hilo.
            App.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        Ui.Responder(Dialogo.Cerrar);
        await App.ArrancarAsync(["--preview-novedades"]);
        App.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Assert.Equal("es", Loc.Language);
    });
#endif

    [Fact]
    public Task Un_error_inesperado_se_apunta_y_se_avisa_una_vez() => Ui.Run(async () =>
    {
        await Ui.Datos();
        var registro = Path.Combine(Ui.Carpeta, "crash.log");
        Ui.Invocar(App, "InstalarGestorDeErrores");
        typeof(App).GetField("_ultimoAviso", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, DateTime.MinValue);

        // Un error en la interfaz: se apunta, se avisa y se sigue.
        await Ui.Hacer(() => Ui.App.Dispatcher.BeginInvoke(() => throw new InvalidOperationException("boom")));
        await Ui.Hasta(() => File.Exists(registro));
        Assert.Contains("boom", File.ReadAllText(registro));
        var aviso = Assert.Single(Ui.Sistema.Avisos);
        Assert.Equal(TipoAviso.Error, aviso.Tipo);
        Assert.Contains(Loc.Get("UnexpectedErrorTitle"), aviso.Texto);
        Ui.Errores.Clear();

        // El segundo, enseguida, solo se apunta.
        App.Avisar();
        Assert.Single(Ui.Sistema.Avisos);

        // Las tareas sin observar y los errores del proceso tambien se apuntan.
        App.AlFallarUnaTarea(null, new UnobservedTaskExceptionEventArgs(new AggregateException(new Exception("tarea"))));
        App.AlFallarElProceso(null, new UnhandledExceptionEventArgs(new Exception("proceso"), false));
        var texto = File.ReadAllText(registro);
        Assert.Contains("tarea", texto);
        Assert.Contains("proceso", texto);

        // El registro no crece sin fin.
        File.WriteAllText(registro, new string('x', 300 * 1024));
        App.Apuntar("prueba", new Exception("nuevo"));
        Assert.True(File.Exists(registro + ".old"));
        Assert.True(new FileInfo(registro).Length < 10 * 1024);

        // Si no se puede escribir, no pasa nada.
        Rutas.Carpeta = Path.Combine(Ui.Carpeta, "no\0valida");
        App.Apuntar("prueba", new Exception("perdido"));
        Rutas.Carpeta = Ui.Carpeta;

        // Si avisar revienta, se apunta.
        typeof(App).GetField("_ultimoAviso", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, DateTime.MinValue);
        Services.Sistema.Actual = new SistemaSinAvisos();
        App.Avisar();
        Services.Sistema.Actual = Ui.Sistema;
        Assert.Contains("sin avisos", File.ReadAllText(registro));
    });

    [Fact]
    public Task Los_textos_del_aviso_de_error_existen_aunque_no_haya_idioma_todavia() => Ui.Run(async () =>
    {
        var datos = await Ui.Datos("en");
        Assert.Equal(Loc.Get("UnexpectedError"), App.Texto("UnexpectedError"));

        var campo = typeof(Loc).GetField("_service", BindingFlags.Static | BindingFlags.NonPublic)!;
        campo.SetValue(null, null);
        try
        {
            Assert.Equal("UnexpectedErrorTitle", Loc.Get("UnexpectedErrorTitle"));
            Assert.Equal("Clave", Loc.Format("Clave", 1));
            Assert.Equal("es", Loc.Language);
            Assert.Throws<InvalidOperationException>(() => Loc.Texts);

            var es = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";
            Assert.Equal(es ? "Algo ha fallado" : "Something went wrong", App.Texto("UnexpectedErrorTitle"));
            Assert.Contains("Task Manager", App.Texto("UnexpectedError"));

            var antes = System.Globalization.CultureInfo.CurrentUICulture;
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(es ? "en-US" : "es-ES");
            Assert.Equal(es ? "Something went wrong" : "Algo ha fallado", App.Texto("UnexpectedErrorTitle"));
            Assert.Contains("Task Manager", App.Texto("UnexpectedError"));
            System.Globalization.CultureInfo.CurrentUICulture = antes;
        }
        finally
        {
            Loc.Use(datos.Store.Texts);
        }
    });

    private sealed class SistemaSinAvisos : ISistema
    {
        public void Aviso(TipoAviso tipo, string texto) => throw new InvalidOperationException("sin avisos");

        public void Abrir(string destino) { }
        public bool HayTexto() => false;
        public string LeerTexto() => string.Empty;
        public void EscribirTexto(string texto) { }
        public bool HayImagen() => false;
        public System.Windows.Media.Imaging.BitmapSource? LeerImagen() => null;
        public bool HayFicheros() => false;
        public IReadOnlyList<string> LeerFicheros() => [];
        public string? ElegirFichero(Window owner, string? filtro) => null;
        public void Arrastrar(DependencyObject origen, object datos) { }
        public IReadOnlyList<System.Drawing.Bitmap> CapturarPantallas() => [];
        public object? LeerRegistro(string clave, string nombre) => null;
        public void EscribirRegistro(string clave, string nombre, string valor) { }
        public void BorrarRegistro(string clave, string nombre) { }
        public bool RegistrarAtajo(IntPtr ventana, int id, uint modificadores, uint tecla) => true;
        public void SoltarAtajo(IntPtr ventana, int id) { }
        public System.Windows.Input.ModifierKeys Teclas => default;
        public System.Windows.Input.MouseButtonState BotonIzquierdo(System.Windows.Input.MouseEventArgs e) => default;
        public Point PosicionRaton(System.Windows.Input.MouseEventArgs e) => default;
        public bool BandejaVisible => false;
    }
}
