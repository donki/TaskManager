using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Desktop.Controls;
using TaskManager.Desktop.Services;
using TaskManager.Desktop.Tests.Banco;
using TaskManager.Tests;

namespace TaskManager.Desktop.Tests;

/// <summary>Ajustes, entrada, calendario, etiquetas, novedades y los dialogos.</summary>
public class VentanasTests
{
    /// <summary>Alguien dentro con Google, sin red: la sesion guardada vale.</summary>
    internal static async Task<SupabaseAuthService> ConGoogleAsync(Datos d, string avatar = "")
    {
        await d.Settings.SetAsync(SettingsService.KeyGoogleSub, "sub-1");
        await d.Settings.SetAsync(SettingsService.KeyAuthProvider, nameof(IdentityProvider.Google));
        await d.Settings.SetAsync(SettingsService.KeyAccountEmail, "ana@correo.es");
        await d.Settings.SetAsync(SettingsService.KeyDisplayName, "Ana");
        await d.Settings.SetAsync(SettingsService.KeyAvatarUrl, avatar);
        var http = new FakeHttp().Throw(HttpMethod.Post, string.Empty).Throw(HttpMethod.Get, string.Empty);
        var tokens = new FakeTokens();
        tokens.Values["auth.google_refresh"] = "refresco";
        var auth = new SupabaseAuthService(http.Client(), d.Settings, tokens, new FakeBrowser());
        Assert.NotNull(await auth.RestoreSessionAsync());
        return auth;
    }

    internal static SupabaseAuthService SinNadie(Datos d, IOAuthBrowser? navegador = null, FakeHttp? http = null, ITokenStore? tokens = null) =>
        new((http ?? new FakeHttp()).Client(), d.Settings, tokens ?? new FakeTokens(), navegador ?? new FakeBrowser());

    // ---------------------------------------------------------------------------------
    // Ajustes
    // ---------------------------------------------------------------------------------

    [Fact]
    public Task Ajustes_enseñan_la_cuenta_de_Google_con_su_foto() => Ui.Run(async () =>
    {
        var datos = await Ui.Datos();
        var foto = Path.Combine(Ui.Carpeta, "foto.png");
        await File.WriteAllBytesAsync(foto, GroupLink.QrPng(new Uri("taskmanager://x")));
        var auth = await ConGoogleAsync(datos, new Uri(foto).AbsoluteUri);

        var ajustes = await Ui.Mostrar(new SettingsWindow(datos.Settings, null, auth, datos.Tasks));
        Assert.Equal("Ana · ana@correo.es (Google)", ajustes.AccountLabel.Text);
        Assert.Equal(Visibility.Visible, ajustes.AvatarCircle.Visibility);
        Assert.True(ajustes.DisplayNameBox.IsReadOnly);
        Assert.True(ajustes.SignOutButton.IsEnabled);
        Assert.Equal(Localization.Loc.Get("SwitchAccountHint"), ajustes.AccountHintLabel.Text);

        // Una foto con una direccion imposible no se enseña, y no tumba la ventana.
        await datos.Settings.SetAsync(SettingsService.KeyAvatarUrl, "no es una direccion");
        await datos.Settings.SetAsync(SettingsService.KeyAuthProvider, "Inventado");
        var otra = await Ui.Mostrar(new SettingsWindow(datos.Settings, null, auth, datos.Tasks));
        Assert.Equal(Visibility.Collapsed, otra.AvatarCircle.Visibility);
        Assert.EndsWith("(Google)", otra.AccountLabel.Text);

        // Cancelar cierra sin guardar.
        await Ui.Llamar(otra, "OnCancelClick", null, new RoutedEventArgs());
        Assert.False(otra.IsVisible);
    });

    [Fact]
    public Task Ajustes_sin_cuenta_y_en_local() => Ui.Run(async () =>
    {
        var datos = await Ui.Datos();
        var auth = SinNadie(datos);
        var vacia = await Ui.Mostrar(new SettingsWindow(datos.Settings, null, auth, datos.Tasks));
        Assert.Equal(Localization.Loc.Get("NoAccountDesktop"), vacia.AccountLabel.Text);
        Assert.False(vacia.SignOutButton.IsEnabled);
        Assert.Equal(Localization.Loc.Get("AccountListsHint"), vacia.AccountHintLabel.Text);
        Assert.Equal(Visibility.Collapsed, vacia.AvatarCircle.Visibility);

        await auth.SignInLocallyAsync();
        var local = await Ui.Mostrar(new SettingsWindow(datos.Settings, null, auth, datos.Tasks));
        Assert.StartsWith(Localization.Loc.Get("LocalAccount"), local.AccountLabel.Text);
        Assert.False(local.DisplayNameBox.IsReadOnly);
        Assert.Equal(Localization.Loc.Get("SignInWithAccount"), local.SignOutButton.ToolTip);

        // En local el nombre se escribe; vacio vuelve a «Yo».
        Ui.ResponderAsync(async w =>
        {
            var a = (SettingsWindow)w;
            a.DisplayNameBox.Text = "  Pepe ";
            await Ui.Pulsar(a.SaveButton);
        });
        await Ui.Hacer(() => Ventanas.Modal(new SettingsWindow(datos.Settings, null, auth, datos.Tasks)));
        Assert.Equal("Pepe", datos.Settings.DisplayName);

        Ui.ResponderAsync(async w =>
        {
            var a = (SettingsWindow)w;
            a.DisplayNameBox.Text = " ";
            await Ui.Pulsar(a.SaveButton);
        });
        await Ui.Hacer(() => Ventanas.Modal(new SettingsWindow(datos.Settings, null, auth, datos.Tasks)));
        Assert.Equal("Yo", datos.Settings.DisplayName);
    });

    [Fact]
    public Task Guardar_los_ajustes_y_el_atajo() => Ui.Run(async () =>
    {
        var datos = await Ui.Datos();
        var auth = await ConGoogleAsync(datos);
        var ventana = new Window();
        var atajo = new GlobalHotkey(new System.Windows.Interop.WindowInteropHelper(ventana).EnsureHandle());
        await datos.Settings.SetAsync(SettingsService.KeySnoozeMinutes, "120");

        // El atajo que pide el usuario esta cogido: se dice, se vuelve al anterior y no se cierra.
        Ui.Sistema.AtajoLibre = false;
        Ui.ResponderAsync(async w =>
        {
            var a = (SettingsWindow)w;
            Assert.Equal(4, a.SnoozeBox.SelectedIndex);
            a.HotkeyBox.Text = "Ctrl+Alt+Q";
            await Ui.Pulsar(a.SaveButton);
            Assert.Equal(Localization.Loc.Format("HotkeyUnavailable", "Ctrl+Alt+Q"), a.StatusLabel.Text);
            Assert.True(a.IsVisible);
            a.Close();
        });
        await Ui.Hacer(() => Ventanas.Modal(new SettingsWindow(datos.Settings, atajo, auth, datos.Tasks)));
        Assert.Equal("Ctrl+Alt+T", datos.Settings.Get(SettingsService.KeyHotkey, "Ctrl+Alt+T"));

        // Libre: se guarda todo, y el inicio con Windows va al registro.
        Ui.Sistema.AtajoLibre = true;
        Ui.ResponderAsync(async w =>
        {
            var a = (SettingsWindow)w;
            a.HotkeyBox.Text = "Ctrl+Alt+Q";
            a.SoundBox.IsChecked = false;
            a.AutoStartBox.IsChecked = true;
            a.NotifyBox.IsChecked = false;
            await Ui.Llamar(a, "OnNotifyToggled", null, new RoutedEventArgs());
            Assert.False(a.SnoozeBox.IsEnabled);
            a.NotifyBox.IsChecked = true;
            await Ui.Llamar(a, "OnNotifyToggled", null, new RoutedEventArgs());
            Assert.True(a.SnoozeBox.IsEnabled);
            a.SnoozeBox.SelectedIndex = 2;
            await Ui.Pulsar(a.SaveButton);
        });
        await Ui.Hacer(() => Ventanas.Modal(new SettingsWindow(datos.Settings, atajo, auth, datos.Tasks)));

        Assert.Equal("Ctrl+Alt+Q", datos.Settings.Get(SettingsService.KeyHotkey));
        Assert.False(datos.Settings.SoundEnabled);
        Assert.Equal(30, datos.Settings.SnoozeMinutes);
        Assert.True(AutoStart.IsEnabled);
        atajo.Dispose();
        ventana.Close();
    });

    [Fact]
    public Task Salir_de_la_cuenta_vuelve_a_pedir_la_entrada() => Ui.Run(async () =>
    {
        var datos = await Ui.Datos();
        var auth = await ConGoogleAsync(datos);
        var ajustes = await Ui.Mostrar(new SettingsWindow(datos.Settings, null, auth, datos.Tasks));

        // Se cierra la puerta sin entrar: la aplicacion se apagaria.
        Ui.Responder(Dialogo.Cerrar);
        await Ui.Pulsar(ajustes.SignOutButton);
        Assert.False(auth.IsSignedIn);
        Assert.Equal(1, Ui.Apagados);
        Assert.Equal(Localization.Loc.Get("NoAccountDesktop"), ajustes.AccountLabel.Text);
        Assert.Contains(Ui.Abiertas, w => w is LoginWindow);
    });

    // ---------------------------------------------------------------------------------
    // Entrada
    // ---------------------------------------------------------------------------------

    private sealed class NavegadorCerrado : IOAuthBrowser
    {
        public string RedirectUri => "http://127.0.0.1:1/auth/";

        public Task<Uri> AuthenticateAsync(Uri authorizeUrl, CancellationToken cancellationToken = default) =>
            Task.FromException<Uri>(new OperationCanceledException());
    }

    private sealed class TokensQueFallan : ITokenStore
    {
        public Task<string?> GetAsync(string key) => Task.FromResult<string?>(null);

        public Task SetAsync(string key, string? value) => Task.FromException(new IOException("disco lleno"));
    }

    private static async Task<LoginWindow> PuertaAsync(SupabaseAuthService auth, Func<LoginWindow, Task> guion)
    {
        var puerta = new LoginWindow(auth);
        Ui.ResponderAsync(async w =>
        {
            await guion(puerta);
            if (puerta.IsVisible)
            {
                puerta.Close();
            }
        });
        await Ui.Hacer(() => Ventanas.Modal(puerta));
        return puerta;
    }

    [Fact]
    public Task Entrar_sin_cuenta_deja_pasar() => Ui.Run(async () =>
    {
        var datos = await Ui.Datos(cuenta: "");
        var auth = SinNadie(datos);

        var puerta = await PuertaAsync(auth, async p =>
        {
            Assert.Equal(Visibility.Visible, p.LocalButton.Visibility);
            Assert.Equal(auth.IsConfigured ? string.Empty : Localization.Loc.Get("OAuthNoClientId"), p.StatusLabel.Text);
            await Ui.Pulsar(p.LocalButton);
        });

        Assert.NotNull(puerta.User);
        Assert.True(auth.IsLocalAccount);
        Assert.Equal(0, Ui.Apagados);
    });

    [Fact]
    public Task Entrar_con_un_proveedor_que_falla_o_se_cancela_lo_dice_y_salir_apaga() => Ui.Run(async () =>
    {
        var datos = await Ui.Datos(cuenta: "");

        // El navegador se cierra sin terminar.
        var cancelada = SinNadie(datos, new NavegadorCerrado());
        var puerta = await PuertaAsync(cancelada, async p =>
        {
            await Ui.Llamar(p, "OnGoogleClick", null, new RoutedEventArgs());
            Assert.Equal(cancelada.IsConfiguredFor(IdentityProvider.Google)
                ? Localization.Loc.Get("SignInCancelled")
                : p.StatusLabel.Text, p.StatusLabel.Text);
            Assert.True(p.GoogleButton.IsEnabled);
            Assert.Equal(Visibility.Collapsed, p.Busy.Visibility);

            // El proveedor contesta con un error.
            await Ui.Llamar(p, "OnMicrosoftClick", null, new RoutedEventArgs());
            Assert.NotEqual(string.Empty, p.StatusLabel.Text);

            // Ni siquiera se puede guardar la entrada sin cuenta.
            await Ui.Pulsar(p.QuitButton);
        });

        Assert.Null(puerta.User);
        Assert.Equal(1, Ui.Apagados);

        var rota = SinNadie(datos, tokens: new TokensQueFallan());
        await PuertaAsync(rota, async p =>
        {
            await Ui.Pulsar(p.LocalButton);
            Assert.Equal("disco lleno", p.StatusLabel.Text);
        });
        Assert.Equal(2, Ui.Apagados);
    });

    [Fact]
    public Task Entrar_con_Google_contra_el_servidor_de_mentira() => Ui.Run(async () =>
    {
        var datos = await Ui.Datos(cuenta: "");
        var http = new FakeHttp();
        var auth = SinNadie(datos, http: http);
        if (!auth.IsConfiguredFor(IdentityProvider.Google))
        {
            return; // Compilacion sin cliente de Google: no hay boton que probar.
        }

        http.OnJson(HttpMethod.Post, "https://oauth2.googleapis.com/token", new
        {
            id_token = Jwt.Make(new { sub = "sub-9", email = "eva@correo.es", name = "Eva" }),
            refresh_token = "r",
            expires_in = 3600,
        });

        var puerta = await PuertaAsync(auth, p => Ui.Pulsar(p.GoogleButton));
        Assert.Equal("sub-9", puerta.User!.Id);
        Assert.Equal(0, Ui.Apagados);
    });

    // ---------------------------------------------------------------------------------
    // Calendario
    // ---------------------------------------------------------------------------------

    private static UIElement Celda(Views.CalendarView vista, int dia) =>
        vista.MonthGrid.Children.OfType<Border>()
            .Single(b => b.Child is StackPanel { Children: [TextBlock t, ..] } && t.Text == dia.ToString());

    [Fact]
    public Task El_calendario_enseña_el_mes_y_crea_tareas_del_dia() => Ui.Run(async () =>
    {
        var datos = await Ui.Datos();
        var lista = await datos.Repo.CreateListAsync("Casa");
        var hoy = DateTime.Today;
        var t = await datos.Repo.AddTaskAsync(lista.Id, "Hoy", plannedFor: hoy);
        t.IsPinned = true;
        await datos.Repo.UpdateTaskAsync(t);
        var hecha = await datos.Repo.AddTaskAsync(lista.Id, "Hecha hoy", plannedFor: hoy);
        await datos.Tasks.CompleteTaskAsync(hecha);
        var grupo = await datos.Repo.SaveGroupAsync(new TaskGroup { Name = "Familia" });
        var deGrupo = await datos.Repo.CreateListAsync("Compra", grupo.Id);
        await datos.Repo.AddTaskAsync(deGrupo.Id, "Leche", plannedFor: hoy);

        var ventana = await Ui.Mostrar(new CalendarWindow(datos.Tasks));
        var vista = ventana.View;
        await Ui.Hasta(() => vista.DayTasks.Items.Count == 3, que: "las tareas de hoy");

        var filas = vista.DayTasks.Items.Cast<Views.CalendarView.DayTaskRow>().ToList();
        Assert.Contains(filas, r => r.Title == "📌 Hoy" && r.ListName == "Casa");
        Assert.Contains(filas, r => r.ListName == "Familia · Compra");
        var filaHecha = filas.Single(r => r.IsDone);
        Assert.Equal(0.55, filaHecha.Opacity);
        Assert.NotNull(filaHecha.Decoration);
        Assert.Null(filas.First(r => !r.IsDone).Decoration);
        Assert.Equal(1.0, filas.First(r => !r.IsDone).Opacity);
        Assert.Equal(7, vista.WeekdayRow.Children.Count);
        Assert.Equal(string.Empty, vista.EmptyLabel.Text);

        // Elegir otro dia (sin nada).
        var otroDia = hoy.Day == 1 ? 2 : 1;
        await Ui.Hacer(() => Celda(vista, otroDia).RaiseEvent(Ui.Raton(Celda(vista, otroDia), UIElement.MouseLeftButtonUpEvent)));
        Assert.Empty(vista.DayTasks.Items);
        Assert.Equal(Localization.Loc.Get("CalendarDayEmpty"), vista.EmptyLabel.Text);

        // Doble clic en el dia: a escribir.
        var doble = Ui.Raton(Celda(vista, otroDia), UIElement.MouseLeftButtonDownEvent);
        typeof(MouseButtonEventArgs).GetProperty("ClickCount")!.SetValue(doble, 2);
        await Ui.Hacer(() => Celda(vista, otroDia).RaiseEvent(doble));
        var simple = Ui.Raton(Celda(vista, otroDia), UIElement.MouseLeftButtonDownEvent);
        await Ui.Hacer(() => Celda(vista, otroDia).RaiseEvent(simple));

        // Crear una tarea ese dia: nace planificada y se abre; guardarla relee el mes.
        Ui.ResponderAsync(async w =>
        {
            await Ui.Calma();
            await Ui.Pulsar(((TaskDetailWindow)w).SaveButton);
        });
        vista.DayAddBox.Text = "Del dia";
        await Ui.Tecla(vista.DayAddBox, Key.Enter);
        var creada = (await datos.Repo.GetAllTasksAsync(TaskFilter.All)).Single(x => x.Title == "Del dia");
        Assert.Equal(new DateTime(hoy.Year, hoy.Month, otroDia), creada.PlannedFor);
        Assert.Single(vista.DayTasks.Items);

        // Sin texto, o con otra tecla, nada; con el boton tambien se crea.
        await Ui.Tecla(vista.DayAddBox, Key.Enter);
        await Ui.Tecla(vista.DayAddBox, Key.Q);
        Ui.Responder(Dialogo.Cerrar);
        vista.DayAddBox.Text = "Otra";
        await Ui.Llamar(vista, "OnDayAddClick", null, new RoutedEventArgs());
        Assert.Equal(2, vista.DayTasks.Items.Count);

        // Doble clic en una tarea del dia la abre.
        Ui.ResponderAsync(async w =>
        {
            await Ui.Calma();
            ((TaskDetailWindow)w).TitleBox.Text = "Renombrada";
            await Ui.Pulsar(((TaskDetailWindow)w).SaveButton);
        });
        vista.DayTasks.SelectedIndex = 0;
        await Ui.Llamar(vista, "OnDayTaskDoubleClick", null, Ui.Raton(vista.DayTasks));
        Assert.Contains(vista.DayTasks.Items.Cast<Views.CalendarView.DayTaskRow>(), r => r.Title == "Renombrada");

        // Sin nada elegido, o con una tarea que ya no existe, no se abre nada.
        vista.DayTasks.SelectedIndex = -1;
        await Ui.Llamar(vista, "OnDayTaskDoubleClick", null, Ui.Raton(vista.DayTasks));
        vista.DayTasks.SelectedIndex = 0;
        var elegida = (Views.CalendarView.DayTaskRow)vista.DayTasks.SelectedItem;
        await datos.Repo.DeleteTaskAsync((await datos.Repo.GetTaskAsync(elegida.Id))!);
        Ui.Responder(Dialogo.Cerrar);
        await Ui.Llamar(vista, "OnDayTaskDoubleClick", null, Ui.Raton(vista.DayTasks));

        // Meses: siguiente, anterior y hoy.
        var mes = vista.MonthLabel.Text;
        await Ui.Llamar(vista, "OnNextMonthClick", null, new RoutedEventArgs());
        Assert.NotEqual(mes, vista.MonthLabel.Text);
        await Ui.Llamar(vista, "OnPreviousMonthClick", null, new RoutedEventArgs());
        await Ui.Llamar(vista, "OnPreviousMonthClick", null, new RoutedEventArgs());
        await Ui.Llamar(vista, "OnTodayClick", null, new RoutedEventArgs());
        Assert.Equal(mes, vista.MonthLabel.Text);
        await vista.RefreshAsync();
    });

    [Fact]
    public Task Un_calendario_sin_datos_no_hace_nada() => Ui.Run(async () =>
    {
        await Ui.Datos();
        var vista = new Views.CalendarView();
        await vista.RefreshAsync();
        vista.DayAddBox.Text = "x";
        await Ui.Llamar(vista, "OnDayAddClick", null, new RoutedEventArgs());
        await Ui.Llamar(vista, "OnDayTaskDoubleClick", null, Ui.Raton(vista.DayTasks));
        await Ui.Hacer(() => Ui.Invocar(vista, "LoadListNamesAsync"));
        Assert.Equal(string.Empty, vista.MonthLabel.Text);
    });

    // ---------------------------------------------------------------------------------
    // Etiquetas y novedades
    // ---------------------------------------------------------------------------------

    [Fact]
    public Task La_ventana_de_etiquetas_cuenta_y_borra() => Ui.Run(async () =>
    {
        var datos = await Ui.Datos();
        var lista = await datos.Repo.CreateListAsync("Casa");
        var t = await datos.Repo.AddTaskAsync(lista.Id, "Viva");
        t.Tags = "casa";
        await datos.Repo.UpdateTaskAsync(t);
        var duena = await Ui.Mostrar(new Window());

        TagsWindow? etiquetas = null;
        Ui.ResponderAsync(async w =>
        {
            etiquetas = (TagsWindow)w;
            await Ui.Hasta(() => Ui.Textos(w).Contains("#casa"));
            Assert.Contains(Localization.Loc.Format("TagsCount", 1, 1), Ui.Textos(w));
            var papelera = Ui.Botones(w).First(b => (string?)b.ToolTip == Localization.Loc.Get("DeleteTag"));

            // Tiene pendientes: se pregunta; cancelar no borra.
            Ui.Responder(Dialogo.Primero);
            await Ui.Hacer(() => papelera.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));
            Assert.Equal(["casa"], await datos.Repo.GetTagsAsync());
            Assert.Contains(Dialogo.Leidos.Last(), x => x == Localization.Loc.Format("DeleteTagPending", "casa", 1, 1));

            Ui.Responder(Dialogo.Aceptar);
            await Ui.Hacer(() => papelera.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));
            Assert.Empty(await datos.Repo.GetTagsAsync());

            // El boton de cerrar.
            await Ui.Pulsar(Ui.Botones(w).Last(b => (string?)b.ToolTip == Localization.Loc.Get("Close")));
        });
        await Ui.Hacer(() => Ventanas.Modal(new TagsWindow(duena, datos.Tasks)));
        Assert.True(etiquetas!.Changed);
    });

    [Fact]
    public Task Las_novedades_enseñan_las_versiones_y_se_dan_por_vistas() => Ui.Run(async () =>
    {
        var datos = await Ui.Datos();
        var version = WhatsNewWindow.CurrentVersion();
        Assert.DoesNotContain('+', version);
        Assert.True(datos.Settings.HasUnseenVersion(version));

        var novedades = await Ui.Mostrar(new WhatsNewWindow(datos.Settings));
        Assert.NotEmpty(Ui.Textos(novedades));
        await Ui.Hasta(() => !datos.Settings.HasUnseenVersion(version));

        await Ui.Pulsar(Ui.Botones(novedades).Last());
        Assert.False(novedades.IsVisible);

        // En ingles tambien.
        var en = await Ui.Datos("en");
        var english = await Ui.Mostrar(new WhatsNewWindow(en.Settings));
        Assert.NotEmpty(Ui.Textos(english));
    });

    // ---------------------------------------------------------------------------------
    // Dialogos
    // ---------------------------------------------------------------------------------

    [Fact]
    public Task Los_dialogos_responden_al_teclado_y_al_doble_clic() => Ui.Run(async () =>
    {
        await Ui.Datos();
        var duena = await Ui.Mostrar(new Window());
        Task Tecla(Window w, Key k) => Ui.Tecla(w, k);

        // Confirmar: Escape es no, Enter es si; otra tecla no hace nada.
        Ui.ResponderAsync(async w => { await Tecla(w, Key.A); await Tecla(w, Key.Escape); });
        Assert.False(ModernDialog.Confirm(duena, "T", "M"));
        Ui.ResponderAsync(w => Tecla(w, Key.Enter));
        Assert.True(ModernDialog.Confirm(duena, "T", "M", danger: true));

        // Aviso: con Escape o Enter se cierra; otra tecla no.
        Ui.ResponderAsync(async w => { await Tecla(w, Key.B); await Tecla(w, Key.Enter); });
        ModernDialog.Alert(duena, "T", "M");

        // Elegir con salida alternativa: Escape es cancelar.
        Ui.ResponderAsync(async w => { await Tecla(w, Key.C); await Tecla(w, Key.Escape); });
        Assert.Null(ModernDialog.Choose(duena, "T", "M", [("a", 1), ("b", 2)], "alt", out var cancelada));
        Assert.True(cancelada);
        Ui.Responder(Dialogo.Elegir(1));
        Assert.Equal(2, ModernDialog.Choose(duena, "T", "M", [("a", 1), ("b", 2)], "alt", out cancelada));
        Assert.False(cancelada);

        // Elegir: doble clic en la lista acepta; Escape cancela; sin fila elegida, nada.
        Ui.ResponderAsync(async w =>
        {
            var lista = Ui.Buscar<ListBox>(w).First();
            lista.SelectedIndex = 1;
            await Ui.Hacer(() => lista.RaiseEvent(Ui.Raton(lista)));
        });
        Assert.Equal(20, ModernDialog.Pick(duena, "T", "M", [("a", 10), ("b", 20)], "ok"));
        Ui.ResponderAsync(async w => { await Tecla(w, Key.D); await Tecla(w, Key.Escape); });
        Assert.Null(ModernDialog.Pick(duena, "T", "M", [("a", 10)], "ok"));
        Ui.ResponderAsync(async w =>
        {
            Ui.Buscar<ListBox>(w).First().SelectedIndex = -1;
            await Ui.Pulsar(Ui.Botones(w).Last());
        });
        Assert.Null(ModernDialog.Pick(duena, "T", "M", [("a", 10)], "ok"));

        // Elegir o escribir: escribir suelta la fila, elegir borra lo escrito.
        Ui.ResponderAsync(async w =>
        {
            var lista = Ui.Buscar<ListBox>(w).First();
            var caja = Ui.Buscar<TextBox>(w).First();
            lista.SelectedIndex = 0;
            caja.Text = "nueva";
            Assert.Equal(-1, lista.SelectedIndex);
            caja.Text = string.Empty;
            lista.SelectedIndex = 1;
            await Ui.Pulsar(Ui.Botones(w).Last());
        });
        Assert.Equal("dos", ModernDialog.PickOrType(duena, "T", "M", ["uno", "dos"], "pista", "ok"));
        Ui.ResponderAsync(async w =>
        {
            var lista = Ui.Buscar<ListBox>(w).First();
            lista.SelectedIndex = 0;
            Ui.Buscar<TextBox>(w).First().Text = "escrita";
            await Ui.Tecla(Ui.Buscar<TextBox>(w).First(), Key.X);
            await Ui.Tecla(Ui.Buscar<TextBox>(w).First(), Key.Enter);
        });
        Assert.Equal("escrita", ModernDialog.PickOrType(duena, "T", "M", ["uno"], "pista", "ok"));
        Ui.ResponderAsync(async w =>
        {
            var lista = Ui.Buscar<ListBox>(w).First();
            lista.SelectedIndex = 0;
            await Ui.Hacer(() => lista.RaiseEvent(Ui.Raton(lista)));
        });
        Assert.Equal("uno", ModernDialog.PickOrType(duena, "T", "M", ["uno"], "pista", "ok"));
        Ui.ResponderAsync(async w => { await Tecla(w, Key.E); await Tecla(w, Key.Escape); });
        Assert.Null(ModernDialog.PickOrType(duena, "T", "M", [], "pista", "ok"));

        // La caja de una linea: Enter acepta; otra tecla no.
        Ui.ResponderAsync(async w =>
        {
            var caja = Ui.Buscar<TextBox>(w).First();
            Assert.Equal("inicial", caja.Text);
            caja.Text = "  final ";
            await Ui.Tecla(caja, Key.F);
            await Ui.Tecla(caja, Key.Enter);
        });
        Assert.Equal("final", Prompt.Ask(duena, "T", "pista", "inicial"));
    });
}
