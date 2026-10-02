using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Desktop.Services;
using TaskManager.Desktop.Tests.Banco;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace TaskManager.Desktop.Tests;

/// <summary>Lo que la aplicacion le pide a Windows, contra el sistema de mentira.</summary>
public class ServiciosTests
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme";
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run\TaskManager";

    // ---------------------------------------------------------------------------------
    // Tema, inicio con Windows y enlaces de invitacion
    // ---------------------------------------------------------------------------------

    [Fact]
    public Task El_tema_sigue_a_Windows_y_pinta_la_barra_de_titulo() => Ui.Run(async () =>
    {
        Ui.Sistema.Registro[Personalize] = 0;
        ThemeManager.Apply();
        Assert.True(ThemeManager.IsDark);
        Assert.Equal(Color.FromRgb(0x14, 0x13, 0x18), ((SolidColorBrush)Ui.App.Resources["PageBackground"]).Color);

        // La barra se pinta cuando la ventana ya tiene handle, y si ya lo tiene, al momento.
        var ventana = new Window();
        ThemeManager.StyleTitleBar(ventana);
        await Ui.Mostrar(ventana);
        ThemeManager.StyleTitleBar(ventana);

        Ui.Sistema.Registro[Personalize] = 1;
        ThemeManager.Apply();
        Assert.False(ThemeManager.IsDark);
        Assert.Equal(Color.FromRgb(0xF8, 0xF9, 0xFA), ((SolidColorBrush)Ui.App.Resources["PageBackground"]).Color);
        ThemeManager.StyleTitleBar(ventana);

        // Sin la clave (politica restrictiva), claro.
        Ui.Sistema.Registro.Clear();
        ThemeManager.Apply();
        Assert.False(ThemeManager.IsDark);

        // Si leer el registro revienta, tambien claro.
        Services.Sistema.Actual = new SistemaQueFalla();
        ThemeManager.Apply();
        Assert.False(ThemeManager.IsDark);
        Services.Sistema.Actual = Ui.Sistema;

        // Y la barra de una ventana que nunca llega a tener handle no se pinta.
        var sinHandle = new Window();
        ThemeManager.StyleTitleBar(sinHandle);
        sinHandle.Close();
    });

    [Fact]
    public Task Inicio_con_Windows_en_la_clave_Run_del_usuario() => Ui.Run(() =>
    {
        Assert.False(AutoStart.IsEnabled);

        AutoStart.Set(true);
        Assert.True(AutoStart.IsEnabled);
        Assert.EndsWith("\" --tray", (string)Ui.Sistema.Registro[Run]);
        Assert.StartsWith($"\"{Environment.ProcessPath}\"", (string)Ui.Sistema.Registro[Run]);

        AutoStart.Set(false);
        Assert.False(AutoStart.IsEnabled);
        Assert.False(Ui.Sistema.Registro.ContainsKey(Run));

        // Un valor de otro programa con el mismo nombre no cuenta.
        Ui.Sistema.Registro[Run] = "otro.exe";
        Assert.False(AutoStart.IsEnabled);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Los_enlaces_de_invitacion_se_registran_y_viajan_por_el_buzon() => Ui.Run(() =>
    {
        GroupLinkProtocol.Registrar();
        var clave = $@"Software\Classes\{GroupLink.Scheme}";
        Assert.Equal("URL:Task Manager", Ui.Sistema.Registro[$@"{clave}\"]);
        Assert.Equal(string.Empty, Ui.Sistema.Registro[$@"{clave}\URL Protocol"]);
        Assert.Equal($"\"{Environment.ProcessPath}\" \"%1\"", Ui.Sistema.Registro[$@"{clave}\shell\open\command\"]);
        Assert.Equal($"\"{Environment.ProcessPath}\",0", Ui.Sistema.Registro[$@"{clave}\DefaultIcon\"]);

        // Si el registro no deja, la aplicacion arranca igual.
        Ui.Sistema.FalloRegistro = new UnauthorizedAccessException();
        GroupLinkProtocol.Registrar();

        var invite = new GroupInvite("ABC", "clave");
        var enlace = GroupLink.For(invite).ToString();
        Assert.Equal(invite, GroupLinkProtocol.EnLosArgumentos(["--tray", "C:\\x.exe", enlace]));
        Assert.Null(GroupLinkProtocol.EnLosArgumentos(["--tray", "https://otra.cosa/"]));

        // El buzon: se deja, se recoge una vez y desaparece.
        Assert.Null(GroupLinkProtocol.Recoger());
        GroupLinkProtocol.Dejar(invite);
        Assert.StartsWith(Ui.Carpeta, GroupLinkProtocol.BuzonPath);
        Assert.Equal(invite, GroupLinkProtocol.Recoger());
        Assert.Null(GroupLinkProtocol.Recoger());

        // Lo que no es una invitacion no se recoge como tal.
        File.WriteAllText(GroupLinkProtocol.BuzonPath, "basura");
        Assert.Null(GroupLinkProtocol.Recoger());

        // Un buzon que no se puede leer ni escribir (es una carpeta) no tumba nada.
        Directory.CreateDirectory(GroupLinkProtocol.BuzonPath);
        GroupLinkProtocol.Dejar(invite);
        Assert.Null(GroupLinkProtocol.Recoger());
        return Task.CompletedTask;
    });

    // ---------------------------------------------------------------------------------
    // QR
    // ---------------------------------------------------------------------------------

    private static Drawing.Bitmap Qr(GroupInvite invite) =>
        new(new MemoryStream(GroupLink.QrPng(GroupLink.For(invite))));

    [Fact]
    public Task El_QR_se_lee_de_un_fichero_del_portapapeles_y_de_la_pantalla() => Ui.Run(async () =>
    {
        var invite = new GroupInvite("QR9", "k9");
        var png = GroupLink.QrPng(GroupLink.For(invite));
        var fichero = Path.Combine(Ui.Carpeta, "qr.png");
        await File.WriteAllBytesAsync(fichero, png);

        Assert.Equal(invite, QrReader.DesdeFichero(fichero));
        Assert.Null(QrReader.DesdeFichero(Path.Combine(Ui.Carpeta, "no-esta.png")));
        var texto = Path.Combine(Ui.Carpeta, "texto.png");
        await File.WriteAllTextAsync(texto, "no soy una imagen");
        Assert.Null(QrReader.DesdeFichero(texto));

        // Una imagen sin QR.
        using (var blanca = new Drawing.Bitmap(50, 50))
        {
            Assert.Null(QrReader.Leer(blanca));
        }

        // Portapapeles: el enlace como texto, la imagen del QR, o nada.
        Assert.Null(QrReader.DesdePortapapeles());
        Ui.Sistema.Texto = "  " + GroupLink.For(invite) + " ";
        Assert.Equal(invite, QrReader.DesdePortapapeles());
        Ui.Sistema.Texto = "otra cosa";
        Assert.Null(QrReader.DesdePortapapeles());
        var imagen = new PngBitmapDecoder(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        Ui.Sistema.Imagen = imagen;
        Assert.Equal(invite, QrReader.DesdePortapapeles());

        // Pantalla: se mira monitor por monitor.
        Assert.Null(QrReader.DesdePantalla());
        Ui.Sistema.Pantallas.Add(new Drawing.Bitmap(40, 40));
        Ui.Sistema.Pantallas.Add(Qr(invite));
        Assert.Equal(invite, QrReader.DesdePantalla());
    });

    // ---------------------------------------------------------------------------------
    // Atajo global
    // ---------------------------------------------------------------------------------

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [Fact]
    public Task El_atajo_global_se_registra_se_suelta_y_avisa() => Ui.Run(() =>
    {
        Assert.Equal((0x2u | 0x1u, (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.T)),
            GlobalHotkey.Parse("Ctrl+Alt+T"));
        Assert.Equal((0x2u | 0x4u | 0x8u, 0u), GlobalHotkey.Parse("control + shift + win"));
        Assert.Equal(0u, GlobalHotkey.Parse("Ctrl+NoEsUnaTecla").Key);

        var ventana = new Window();
        var handle = new WindowInteropHelper(ventana).EnsureHandle();
        var atajo = new GlobalHotkey(handle);
        var pulsado = 0;
        atajo.Pressed += (_, _) => pulsado++;

        Assert.True(atajo.Register("Ctrl+Alt+T"));
        Assert.Equal((handle, 0x2u | 0x1u | 0x4000u, (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.T)), Ui.Sistema.Atajos[0]);

        // Sin tecla no se pide nada (y se suelta el anterior).
        Assert.False(atajo.Register("Ctrl+Alt"));
        Assert.Equal(1, Ui.Sistema.Soltados);
        Assert.Single(Ui.Sistema.Atajos);

        // Cogido por otra aplicacion.
        Ui.Sistema.AtajoLibre = false;
        Assert.False(atajo.Register("Ctrl+Alt+Y"));
        atajo.Unregister();
        Assert.Equal(1, Ui.Sistema.Soltados);

        // El mensaje del sistema dispara el aviso; otro identificador, no.
        Ui.Sistema.AtajoLibre = true;
        atajo.Register("Ctrl+Alt+T");
        SendMessage(handle, 0x0312, (IntPtr)0xA71, IntPtr.Zero);
        SendMessage(handle, 0x0312, (IntPtr)0x1, IntPtr.Zero);
        SendMessage(handle, 0x0010 + 1, IntPtr.Zero, IntPtr.Zero);
        Assert.Equal(1, pulsado);

        atajo.Dispose();
        Assert.Equal(2, Ui.Sistema.Soltados);
        SendMessage(handle, 0x0312, (IntPtr)0xA71, IntPtr.Zero);
        Assert.Equal(1, pulsado);

        // Una ventana sin handle no vale.
        Assert.Throws<InvalidOperationException>(() => new GlobalHotkey(GetDesktopWindow()));
        ventana.Close();
        return Task.CompletedTask;
    });

    // ---------------------------------------------------------------------------------
    // Bandeja
    // ---------------------------------------------------------------------------------

    [Fact]
    public Task El_icono_de_la_bandeja_cuenta_avisa_y_tiene_su_menu() => Ui.Run(async () =>
    {
        await Ui.Datos();
        using var bandeja = new TrayIconHost();
        Assert.Equal(0, bandeja.Pending);
        Assert.Equal(Localization.Loc.Get("TrayUpToDate"), bandeja.Tooltip);

        bandeja.SetPending(1);
        Assert.Equal(Localization.Loc.Get("TrayOnePending"), bandeja.Tooltip);
        bandeja.SetPending(12);
        Assert.Equal(12, bandeja.Pending);
        Assert.Equal(Localization.Loc.Format("TrayManyPending", 12), bandeja.Tooltip);
        bandeja.SetPending(3);

        bandeja.Notify("Titulo", "Texto");
        Assert.Equal(("Titulo", "Texto"), bandeja.LastNotification);

        var pedidos = new List<string>();
        bandeja.Activated += (_, _) => pedidos.Add("abrir");
        bandeja.MainRequested += (_, _) => pedidos.Add("principal");
        bandeja.SettingsRequested += (_, _) => pedidos.Add("ajustes");
        bandeja.WhatsNewRequested += (_, _) => pedidos.Add("novedades");
        bandeja.ExitRequested += (_, _) => pedidos.Add("salir");

        foreach (var item in bandeja.Menu!.Items.OfType<WinForms.ToolStripMenuItem>())
        {
            item.PerformClick();
        }

        Assert.Equal(["abrir", "principal", "ajustes", "novedades", "salir"], pedidos);

        bandeja.OnMouseClick(WinForms.MouseButtons.Right);
        bandeja.OnMouseClick(WinForms.MouseButtons.Left);
        Assert.Equal("abrir", pedidos.Last());
        Assert.Equal(6, pedidos.Count);

        // Al cambiar de idioma se rehace el menu, con los textos nuevos.
        var antes = bandeja.Menu;
        bandeja.RebuildMenu();
        Assert.NotSame(antes, bandeja.Menu);
        Assert.Equal(Localization.Loc.Get("TrayExit"), bandeja.Menu!.Items[^1].Text);
    });

    [Fact]
    public Task El_icono_de_las_ventanas_sale_del_ejecutable_o_se_dibuja() => Ui.Run(() =>
    {
        var delExe = TrayIconHost.CreateWindowIcon();
        Assert.True(delExe.Width > 0);

        var dibujado = TrayIconHost.IconFrom(null);
        Assert.Equal(32, ((BitmapSource)dibujado).PixelWidth);
        Assert.Equal(32, ((BitmapSource)TrayIconHost.IconFrom(Path.Combine(Ui.Carpeta, "no.exe"))).PixelWidth);
        return Task.CompletedTask;
    });

    // ---------------------------------------------------------------------------------
    // Recordatorios
    // ---------------------------------------------------------------------------------

    [Fact]
    public Task Los_recordatorios_avisan_una_vez_a_la_hora_y_de_lo_que_vence_hoy() => Ui.Run(async () =>
    {
        var datos = await Ui.Datos();
        var lista = await datos.Repo.CreateListAsync("Casa");
        var hoy = await datos.Repo.AddTaskAsync(lista.Id, "Vence hoy");
        hoy.DueAt = DateTime.Today.AddHours(18);
        await datos.Repo.UpdateTaskAsync(hoy);
        await datos.Settings.SetBoolAsync(SettingsService.KeyNotifyEnabled, true);

        using var bandeja = new TrayIconHost();
        using var avisos = new ReminderScheduler(datos.Repo, datos.Settings, bandeja);
        var hora = datos.Settings.NotifyHour;
        var dia = DateTime.Today;

        // Antes de la hora y antes de las 9, nada.
        await avisos.CheckAsync(dia.AddHours(Math.Min(hora, 9) - 1));
        Assert.Null(bandeja.LastNotification);

        // A la hora: el resumen (una pendiente).
        await avisos.CheckAsync(dia.AddHours(Math.Max(hora, 9)).AddMinutes(1));
        Assert.NotNull(bandeja.LastNotification);

        // El de la tarea que vence hoy, una sola vez.
        await avisos.CheckAsync(dia.AddHours(Math.Max(hora, 9)).AddMinutes(2));
        Assert.Equal((Localization.Loc.Get("DueToday"), "Vence hoy"), bandeja.LastNotification);
        bandeja.Notify("x", "y");
        await avisos.CheckAsync(dia.AddHours(Math.Max(hora, 9)).AddMinutes(3));
        Assert.Equal(("x", "y"), bandeja.LastNotification);

        // Al dia siguiente, el resumen con dos pendientes vuelve a salir.
        await datos.Repo.AddTaskAsync(lista.Id, "Otra");
        await avisos.CheckAsync(dia.AddDays(1).AddHours(Math.Max(hora, 9)).AddMinutes(1));
        Assert.Contains(bandeja.LastNotification!.Value.Message, new[]
        {
            Localization.Loc.Format("NotifyManyPending", 2),
        });

        // Con posposicion, se repite cada tantos minutos.
        await datos.Settings.SetAsync(SettingsService.KeySnoozeMinutes, "15");
        bandeja.Notify("x", "y");
        await avisos.CheckAsync(dia.AddDays(1).AddHours(Math.Max(hora, 9)).AddMinutes(10));
        Assert.Equal(("x", "y"), bandeja.LastNotification);
        await avisos.CheckAsync(dia.AddDays(1).AddHours(Math.Max(hora, 9)).AddMinutes(20));
        Assert.Equal(Localization.Loc.Format("NotifyManyPending", 2), bandeja.LastNotification!.Value.Message);

        // Una sola pendiente.
        foreach (var t in (await datos.Repo.GetAllTasksAsync(TaskFilter.Pending)).Skip(1))
        {
            await datos.Tasks.CompleteTaskAsync(t);
        }

        await avisos.CheckAsync(dia.AddDays(1).AddHours(Math.Max(hora, 9)).AddMinutes(40));
        Assert.Equal(Localization.Loc.Get("NotifyOnePending"), bandeja.LastNotification!.Value.Message);

        // Sin avisos, nada.
        await datos.Settings.SetBoolAsync(SettingsService.KeyNotifyEnabled, false);
        bandeja.Notify("x", "y");
        await avisos.CheckAsync(dia.AddDays(2).AddHours(12));
        Assert.Equal(("x", "y"), bandeja.LastNotification);

        // Un fallo de la base no tumba nada.
        await datos.Settings.SetBoolAsync(SettingsService.KeyNotifyEnabled, true);
        await datos.Store.Db.Connection.CloseAsync();
        await avisos.CheckAsync(dia.AddDays(3).AddHours(12));
    });

    [Fact]
    public Task Los_recordatorios_miran_el_reloj_solos() => Ui.Run(async () =>
    {
        var datos = await Ui.Datos();
        await datos.Settings.SetBoolAsync(SettingsService.KeyNotifyEnabled, false);
        using var bandeja = new TrayIconHost();
        var antes = ReminderScheduler.Interval;
        ReminderScheduler.Interval = TimeSpan.FromMilliseconds(20);
        try
        {
            using var avisos = new ReminderScheduler(datos.Repo, datos.Settings, bandeja);
            await Task.Delay(200);
        }
        finally
        {
            ReminderScheduler.Interval = antes;
        }

        Assert.Null(bandeja.LastNotification);
    });

    // ---------------------------------------------------------------------------------
    // Tokens y navegador de la entrada
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Los_tokens_van_cifrados_y_un_fichero_roto_es_como_no_tener_sesion()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "tm-desktop-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);

        var almacen = new DpapiTokenStore(carpeta);
        Assert.Null(await almacen.GetAsync("a"));
        await almacen.SetAsync("a", "secreto");
        Assert.Equal("secreto", await almacen.GetAsync("a"));
        Assert.DoesNotContain("secreto", await File.ReadAllTextAsync(Path.Combine(carpeta, "tokens.dat")));

        // Se relee de disco.
        Assert.Equal("secreto", await new DpapiTokenStore(carpeta).GetAsync("a"));

        // Vaciar lo quita.
        await almacen.SetAsync("a", null);
        Assert.Null(await new DpapiTokenStore(carpeta).GetAsync("a"));

        // Un valor manipulado no se descifra.
        await File.WriteAllTextAsync(Path.Combine(carpeta, "tokens.dat"), "{\"a\":\"AAAA\"}");
        Assert.Null(await new DpapiTokenStore(carpeta).GetAsync("a"));

        // Un fichero que no es JSON se trata como vacio.
        await File.WriteAllTextAsync(Path.Combine(carpeta, "tokens.dat"), "basura");
        Assert.Null(await new DpapiTokenStore(carpeta).GetAsync("a"));
    }

    [Fact]
    public Task El_navegador_del_sistema_vuelve_al_servidor_local() => Ui.Run(async () =>
    {
        // Con el puerto preferido ocupado se coge otro.
        TcpListener? ocupado = null;
        try
        {
            ocupado = new TcpListener(IPAddress.Loopback, 53682);
            ocupado.Start();
        }
        catch (SocketException)
        {
            ocupado = null;
        }

        var navegador = new LoopbackOAuthBrowser();
        Assert.DoesNotContain(":53682/", navegador.RedirectUri);
        ocupado?.Stop();
        Assert.Matches(@"^http://127\.0\.0\.1:\d+/auth/$", new LoopbackOAuthBrowser().RedirectUri);

        // El «navegador» abre la direccion y vuelve con el codigo; se le contesta con la pagina.
        string? pagina = null;
        Ui.Sistema.AlAbrir = url => _ = Task.Run(async () =>
        {
            using var http = new HttpClient();
            pagina = await http.GetStringAsync(navegador.RedirectUri + "?code=abc&state=1");
        });

        var vuelta = await navegador.AuthenticateAsync(new Uri("https://accounts.example/authorize?x=1"));
        Assert.Equal("abc", System.Web.HttpUtility.ParseQueryString(vuelta.Query)["code"]);
        Assert.Equal("https://accounts.example/authorize?x=1", Ui.Sistema.Abiertos.Single());
        await Ui.Hasta(() => pagina is not null);
        Assert.Contains("Ya estás dentro", pagina);

        // Cerrar el navegador sin entrar: se cancela en vez de esperar para siempre.
        Ui.Sistema.AlAbrir = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LoopbackOAuthBrowser().AuthenticateAsync(new Uri("https://accounts.example/"), cts.Token));
    });

    /// <summary>Un sistema cuyo registro no se deja leer.</summary>
    private sealed class SistemaQueFalla : ISistema
    {
        private readonly SistemaFalso _resto = new();

        public object? LeerRegistro(string clave, string nombre) => throw new System.Security.SecurityException();

        public void Abrir(string destino) => _resto.Abrir(destino);
        public bool HayTexto() => false;
        public string LeerTexto() => string.Empty;
        public void EscribirTexto(string texto) { }
        public bool HayImagen() => false;
        public BitmapSource? LeerImagen() => null;
        public bool HayFicheros() => false;
        public IReadOnlyList<string> LeerFicheros() => [];
        public string? ElegirFichero(Window owner, string? filtro) => null;
        public void Aviso(TipoAviso tipo, string texto) { }
        public void Arrastrar(DependencyObject origen, object datos) { }
        public IReadOnlyList<Drawing.Bitmap> CapturarPantallas() => [];
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
