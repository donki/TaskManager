using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Graphics;
using TaskManager.Core.Gamification;
using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Controls;
using TaskManager.Mobile.Helpers;
using TaskManager.Mobile.Localization;
using TaskManager.Mobile.Models;
using TaskManager.Mobile.Pages;
using TaskManager.Mobile.Services;
using TaskManager.Mobile.Tests.Infra;
using TaskManager.Tests;

namespace TaskManager.Mobile.Tests;

/// <summary>La aplicacion, el Shell y su menu, atras, Acerca de, Novedades, filas y celebracion.</summary>
public class ShellAndAppTests
{
    // -----------------------------------------------------------------------
    // Aplicacion
    // -----------------------------------------------------------------------

    private static Window CreateWindow(App app) => (Window)((IApplication)app).CreateWindow(null!);

    [Fact]
    public void La_ventana_carga_los_ajustes_y_la_sincronizacion_sigue_a_la_ventana() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync(language: "en");
        var window = CreateWindow(app.Application!);
        Assert.IsType<AppShell>(window.Page);
        Assert.Equal("en", app.Settings.Get(SettingsService.KeyLanguage));

        ((IWindow)window).Created();
        ((IWindow)window).Resumed();
        ((IWindow)window).Stopped();
        await UiThread.IdleAsync();
        Assert.Empty(app.Ui.Routes);

        // Una tarea nueva de otro dispositivo se avisa con su titulo.
        var coordinator = app.Services.GetRequiredService<SyncCoordinator>();
        await coordinator.Raise("TaskArrived", new ArrivedTask(Guid.NewGuid(), "Pan"));
        await UiThread.IdleAsync();
        var shown = app.Reminders.Shown.Single();
        Assert.Equal(app.Texts["MenuMyTasks"], shown.Title);
        Assert.Equal(app.Texts.Format("TaskArrivedFromDevice", "Pan"), shown.Text);
    });

    [Fact]
    public void Al_volver_con_una_invitacion_pendiente_va_a_los_grupos() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var window = CreateWindow(app.Application!);
        Assert.Same(window.Page, Shell.Current);

        GroupInviteLinks.Anotar(GroupLink.For(new GroupInvite("ABC123", "k")));
        ((IWindow)window).Resumed();
        await UiThread.IdleAsync();
        Assert.Equal(["//GroupsPage"], app.Ui.Routes);

        // Si la navegacion falla, no se cae nada.
        app.Ui.NavigationFails = new InvalidOperationException("sin Shell");
        ((IWindow)window).Resumed();
        await UiThread.IdleAsync();
        Assert.Single(app.Ui.Routes);
        Assert.NotNull(GroupInviteLinks.Recoger());
    });

    [Fact]
    public void Sin_contenedor_la_aplicacion_arranca_igual() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var real = ServiceHelper.Services!;
        var empty = new ServiceCollection()
            .AddSingleton(app.Services.GetRequiredService<LocalizationService>())
            .BuildServiceProvider();
        ServiceHelper.Initialize(empty);
        try
        {
            var window = CreateWindow(new App());
            ((IWindow)window).Created();
            await UiThread.IdleAsync();
            Assert.IsType<AppShell>(window.Page);
        }
        finally
        {
            ServiceHelper.Initialize(real);
        }
    });

    [Fact]
    public void Los_textos_del_aviso_de_error_salen_del_idioma_de_la_aplicacion() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync(language: "en");
        Assert.Equal("en", MauiProgram.Texto(null));
        Assert.Equal(app.Texts["UnexpectedError"], MauiProgram.Texto("UnexpectedError"));

        // Antes de que haya contenedor: el texto por defecto de CrashGuard.
        var real = ServiceHelper.Services!;
        ServiceHelper.Initialize(new ServiceCollection().BuildServiceProvider());
        Assert.Null(MauiProgram.Texto("UnexpectedError"));
        ServiceHelper.Initialize(new BrokenProvider());
        Assert.Null(MauiProgram.Texto(null));
        ServiceHelper.Initialize(real);
    });

    private sealed class BrokenProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => throw new InvalidOperationException("roto");
    }

    [Fact]
    public void El_contenedor_de_la_aplicacion_resuelve_todas_las_pantallas() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var services = MauiProgram.AddServices(new ServiceCollection(), app.Folder)
            .AddSingleton<IReminderPlatform>(new FakeReminders())
            .BuildServiceProvider();

        Assert.IsType<ReminderScheduler>(services.GetRequiredService<INotificationService>());
        Assert.IsType<SupabaseSyncService>(services.GetRequiredService<ISyncService>());
        Assert.IsType<MauiOAuthBrowser>(services.GetRequiredService<IOAuthBrowser>());
        Assert.IsType<SecureTokenStore>(services.GetRequiredService<ITokenStore>());
        Assert.NotNull(services.GetRequiredService<MailOAuthService>());
        Assert.NotNull(services.GetRequiredService<SyncCoordinator>());
        Assert.Equal(12, services.GetRequiredService<HttpClient>().Timeout.TotalSeconds);
        Assert.EndsWith(MauiProgram.DatabaseName, services.GetRequiredService<TaskManager.Core.Data.LocalDatabase>().Connection.DatabasePath);
        await services.GetRequiredService<TaskManager.Core.Data.LocalDatabase>().Connection.CloseAsync();
    });

    // -----------------------------------------------------------------------
    // Shell
    // -----------------------------------------------------------------------

    [Fact]
    public void El_menu_navega_antes_de_cerrarse_y_enseña_la_version() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var shell = new AppShell();

        Assert.False(shell.Named<VisualElement>("MailMenuRow").IsVisible);
        Assert.True(shell.Named<VisualElement>("GroupsMenuRow").IsVisible);
        Assert.Equal($"v{app.Ui.VersionString}", shell.Named<Label>("VersionLabel").Text);

        var handlers = new Dictionary<string, string>
        {
            ["OnMyTasksTapped"] = "//MyTasksPage",
            ["OnCalendarTapped"] = "//CalendarPage",
            ["OnListsTapped"] = "//ListsPage",
            ["OnKanbanTapped"] = "//KanbanPage",
            ["OnMailTapped"] = "//MailPage",
            ["OnGroupsTapped"] = "//GroupsPage",
            ["OnSettingsTapped"] = "//SettingsPage",
            ["OnAboutTapped"] = "//AboutPage",
            ["OnWhatsNewTapped"] = "//WhatsNewPage",
        };

        foreach (var (handler, route) in handlers)
        {
            shell.FlyoutIsPresented = true;
            await shell.Handler(handler, null, new TappedEventArgs(null));
            Assert.Equal(route, app.Ui.Routes.Last());
            Assert.False(shell.FlyoutIsPresented);
        }
    });

    [Fact]
    public void Atras_cierra_el_menu_vuelve_a_inicio_u_oculta() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var shell = new AppShell();

        // Menu abierto: lo cierra.
        shell.FlyoutIsPresented = true;
        Assert.True(shell.SendBackButtonPressed());
        Assert.False(shell.FlyoutIsPresented);

        // En la entrada: oculta la aplicacion.
        Assert.True(shell.SendBackButtonPressed());
        Assert.Equal(1, app.Ui.Hidden);

        // Con una pantalla apilada: vuelve a la anterior.
        var detail = new ContentPage();
        await shell.Navigation.PushAsync(detail);
        Assert.Equal(2, shell.Navigation.NavigationStack.Count);
        shell.SendBackButtonPressed();
        await UiThread.IdleAsync();
        Assert.Equal(1, app.Ui.Hidden);

        // En otra pantalla del menu: vuelve a «Mis tareas».
        shell.CurrentItem = shell.Items.Single(i => i.CurrentItem?.CurrentItem?.Route == "CalendarPage");
        Assert.True(shell.SendBackButtonPressed());
        await Task.Delay(10);
        await UiThread.IdleAsync();
        Assert.Equal(["//MyTasksPage"], app.Ui.Routes);
    });

    // -----------------------------------------------------------------------
    // Atras: lo que se cierra antes
    // -----------------------------------------------------------------------

    private sealed class Handled(bool answer) : ContentPage, IBackHandler
    {
        public int Calls { get; private set; }

        public bool HandleBack()
        {
            Calls++;
            return answer;
        }
    }

    [Fact]
    public void Decide_que_hace_atras() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();

        Assert.Equal(BackAction.Hide, BackNavigation.Decide(null, 1, "MyTasksPage"));
        Assert.Equal(BackAction.Hide, BackNavigation.Decide(new ContentPage(), 1, "LoginPage"));
        Assert.Equal(BackAction.GoHome, BackNavigation.Decide(new ContentPage(), 1, "SettingsPage"));
        Assert.Equal(BackAction.GoHome, BackNavigation.Decide(null, 0, null));
        Assert.Equal(BackAction.Pop, BackNavigation.Decide(new ContentPage(), 2, "MyTasksPage"));

        // La pantalla se queda el boton, o lo deja pasar.
        var keeps = new Handled(true);
        Assert.Equal(BackAction.Handled, BackNavigation.Decide(keeps, 2, "MyTasksPage"));
        var passes = new Handled(false);
        Assert.Equal(BackAction.Pop, BackNavigation.Decide(passes, 2, "MyTasksPage"));
        Assert.Equal(1, passes.Calls);
    });

    [Fact]
    public void Atras_cierra_el_dialogo_abierto_como_tocar_fuera() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var real = new MauiUiPlatform();
        var page = new ContentPage { Content = new Label { Text = "contenido" } };
        TestHandler.Attach(page);

        Assert.False(BackNavigation.CloseDialog(page));        // nada abierto
        Assert.False(BackNavigation.CloseDialog(null));
        Assert.False(BackNavigation.CloseDialog(new NavigationPage()));

        // El dialogo de verdad (ModernDialog): atras lo descarta.
        var alert = real.AlertAsync(page, "Titulo", "Mensaje", "Si", "No");
        Assert.Equal(BackAction.Handled, BackNavigation.Decide(page, 1, "MyTasksPage"));
        Assert.False(await alert);

        var sheet = real.ActionSheetAsync(page, "Elige", "Cancelar", "A", "B");
        Assert.True(BackNavigation.CloseDialog(page));
        Assert.Null(await sheet);

        var prompt = real.PromptAsync(page, "Nombre", null, "OK", "Cancelar", "inicial", "pista");
        Assert.True(BackNavigation.CloseDialog(page));
        Assert.Null(await prompt);
        await UiThread.IdleAsync();
        Assert.Single(((Grid)page.Content).Children);

        // Con la rejilla ya puesta pero sin dialogo: nada que cerrar.
        Assert.False(BackNavigation.CloseDialog(page));

        // Un velo sin su toque (de otra version): se quita sin mas.
        ((Grid)page.Content).Add(new Grid { StyleId = "__modernDialogOverlay" });
        Assert.True(BackNavigation.CloseDialog(page));
        Assert.Single(((Grid)page.Content).Children);
    });

    [Fact]
    public void Fuera_del_movil_el_sistema_real_dice_que_no_esta() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var real = new MauiUiPlatform();
        var page = new ContentPage();

        // Lo que solo existe en el movil no se finge: MAUI responde que aqui no esta.
        Assert.ThrowsAny<Exception>(() => real.VersionString);
        Assert.ThrowsAny<Exception>(() => real.CacheDirectory);
        Assert.ThrowsAny<Exception>(() => real.BeginInvokeOnMainThread(() => { }));
        Assert.ThrowsAny<Exception>(() => real.Haptic(true));
        await Assert.ThrowsAnyAsync<Exception>(() => real.GoToAsync("//MyTasksPage"));
        await Assert.ThrowsAnyAsync<Exception>(() => real.SetClipboardTextAsync("x"));
        await Assert.ThrowsAnyAsync<Exception>(() => real.OpenBrowserAsync("https://example.com"));
        await Assert.ThrowsAnyAsync<Exception>(() => real.OpenUriAsync(new Uri("https://example.com")));
        await Assert.ThrowsAnyAsync<Exception>(() => real.OpenFileAsync("f", Path.GetTempFileName()));
        await Assert.ThrowsAnyAsync<Exception>(() => real.ComposeEmailAsync("Asunto", "a@b.c"));
        await Assert.ThrowsAnyAsync<Exception>(() => real.PickFileAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => real.CameraAllowedAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => real.ShareTextAsync("t", "s", "t"));

        // Apilar y quitar si funcionan sin Shell: es la pila propia de la pagina.
        var other = new ContentPage();
        await real.PushAsync(page, other);
        Assert.Same(other, page.Navigation.NavigationStack.Last());
        await real.PopAsync(page);
        Assert.DoesNotContain(other, page.Navigation.NavigationStack);
        Assert.Null(await real.ReadClipboardImageAsync());   // sin Android no hay imagen que leer
        real.HideApp();                                        // y nada que ocultar
    });

    // -----------------------------------------------------------------------
    // Sesion en el almacen seguro y navegador de la entrada
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Sin_almacen_seguro_los_tokens_caen_a_los_ajustes()
    {
        var fallback = new FakeTokens();
        var store = new SecureTokenStore(fallback);

        await store.SetAsync("k", "valor");
        Assert.Equal("valor", fallback.Values["k"]);
        Assert.Equal("valor", await store.GetAsync("k"));
        await store.SetAsync("k", null);
        Assert.Null(await store.GetAsync("k"));
    }

    [Fact]
    public async Task El_navegador_de_la_entrada_vuelve_por_la_direccion_que_pide_la_autorizacion()
    {
        var browser = new MauiOAuthBrowser();
        Assert.Equal("com.socratic.taskmanager://auth", browser.RedirectUri);

        // Fuera del movil no hay WebAuthenticator; lo que se comprueba es que llega hasta el.
        await Assert.ThrowsAnyAsync<Exception>(() => browser.AuthenticateAsync(
            new Uri("https://accounts.google.com/o/oauth2/v2/auth?client_id=x&redirect_uri=com.googleusercontent.apps.x%3A%2Foauth2redirect&scope=a")));
        await Assert.ThrowsAnyAsync<Exception>(() => browser.AuthenticateAsync(new Uri("https://login.example.com/authorize?x")));
    }

    // -----------------------------------------------------------------------
    // Acerca de y Novedades
    // -----------------------------------------------------------------------

    [Fact]
    public void Acerca_de_cambia_el_idioma_y_rehace_el_Shell() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var window = CreateWindow(app.Application!);
        var first = window.Page;
        var page = new AboutPage();
        Assert.Equal($"v{app.Ui.VersionString}", page.Named<Label>("VersionLabel").Text);
        Assert.Equal(1, page.Named<Button>("SpanishButton").Opacity);
        Assert.Equal(0.5, page.Named<Button>("EnglishButton").Opacity);

        var changes = new List<string?>();
        Loc.Instance.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        await page.Handler("OnEnglishClicked");
        Assert.Equal("en", app.Settings.Get(SettingsService.KeyLanguage));
        Assert.Equal("en", Loc.Instance.Language);
        Assert.Equal(1, page.Named<Button>("EnglishButton").Opacity);
        Assert.NotSame(first, window.Page);
        Assert.Contains("Item[]", changes);
        Assert.Equal(app.Texts["MenuMyTasks"], Loc.Instance["MenuMyTasks"]);

        await page.Handler("OnSpanishClicked");
        Assert.Equal("es", app.Settings.Get(SettingsService.KeyLanguage));
    });

    [Fact]
    public void Acerca_de_abre_la_tienda_el_correo_y_las_novedades() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var page = new AboutPage();

        await page.Handler("OnWindowsClicked");
        Assert.StartsWith("browser:https://apps.microsoft.com/", app.Ui.Opened.Single());
        await page.Handler("OnContactClicked");
        Assert.Equal("mail:jsoladelarosa@gmail.com:Task Manager", app.Ui.Opened.Last());
        await page.Handler("OnWhatsNewClicked");
        Assert.Equal(["//WhatsNewPage"], app.Ui.Routes);

        // Sin navegador ni correo: se copia la direccion (y del correo se avisa).
        app.Ui.OpenFails = new InvalidOperationException("no hay");
        await page.Handler("OnWindowsClicked");
        await page.Handler("OnContactClicked");
        Assert.Equal(["https://apps.microsoft.com/detail/9PHJK2391727", "jsoladelarosa@gmail.com"], app.Ui.Clipboard);
        Assert.Equal("Dirección copiada: jsoladelarosa@gmail.com", app.Ui.Dialogs.Single().Message);
    });

    [Fact]
    public void Novedades_marca_la_version_vista_y_pinta_las_versiones() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var page = new WhatsNewPage();
        await page.Appear();

        Assert.False(app.Settings.HasUnseenVersion(app.Ui.VersionString));
        var list = page.Field<VerticalStackLayout>("_list");
        Assert.Equal(WhatsNew.Load("es").Count, list.Count);
        Assert.Equal(app.Texts["MenuWhatsNew"], page.Title);

        page.Fill("1.0", [new WhatsNew.Release("1.0", new DateTime(2026, 10, 2), ["Una cosa", "Otra"]), new WhatsNew.Release("0.9", null, [])]);
        Assert.Equal(2, list.Count);
        var current = (VerticalStackLayout)((Border)list[0]).Content!;
        Assert.Equal(app.Texts.Format("WhatsNewCurrent", "1.0"), ((Label)current[0]).Text);
        Assert.Equal(4, current.Count);   // version, fecha y dos novedades
        Assert.Single((VerticalStackLayout)((Border)list[1]).Content!);

        page.Fill("1.0", []);
        Assert.Equal(app.Texts["WhatsNewEmpty"], ((Label)list.Single()).Text);
    });

    // -----------------------------------------------------------------------
    // Filas
    // -----------------------------------------------------------------------

    [Fact]
    public void La_fila_de_tarea_enseña_lo_que_la_tarea_tiene() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var task = new TaskItem
        {
            Title = "Pagar",
            IsPinned = true,
            Tags = TaskTags.FromInput("casa, banco"),
            PlannedFor = DateTime.Today,
            DueAt = new DateTime(2026, 12, 24),
            RecurrenceRule = new Recurrence(RecurrenceKind.Weekly, 1).Serialize(),
            MyDayOn = DateTime.Now.Date,
        };
        var row = new TaskRow(task, "Casa");

        Assert.Equal("📌 Pagar", row.Title);
        Assert.True(row.ShowListName);
        Assert.Equal("#" + string.Join("  #", task.TagList), row.TagsCaption);
        Assert.Equal(2, task.TagList.Count);
        Assert.True(row.HasTags);
        Assert.StartsWith(app.Texts.Format("PlanShort", app.Texts["TodayWord"]), row.ScheduleCaption);
        Assert.Contains(app.Texts.Format("DueShort", new DateTime(2026, 12, 24).ToString("d MMM")), row.ScheduleCaption);
        Assert.EndsWith(task.Recurrence.Describe(app.Texts).ToLowerInvariant(), row.ScheduleCaption);
        Assert.True(row.HasSchedule);
        Assert.True(row.InMyDay);
        Assert.Equal("ic_day.png", row.MyDayIcon);
        Assert.Equal("ic_circle.png", row.StateIcon);
        Assert.Equal(TextDecorations.None, row.Decoration);
        Assert.Equal(1.0, row.Opacity);
        Assert.False(row.HasSteps);
        Assert.Equal(string.Empty, row.StepsCaption);
        Assert.False(row.ShowSteps);
        Assert.Equal(task.Progress, row.Progress);
        Assert.Equal(task.Id, row.Id);
        Assert.False(row.IsDone);

        var plain = new TaskRow(new TaskItem { Title = "Hecha", IsDone = true, StepCount = 3, StepsDone = 1 });
        Assert.Equal("Hecha", plain.Title);
        Assert.False(plain.ShowListName);
        Assert.Equal(string.Empty, plain.TagsCaption);
        Assert.False(plain.HasSchedule);
        Assert.Equal("ic_star.png", plain.MyDayIcon);
        Assert.Equal("ic_circle_check.png", plain.StateIcon);
        Assert.Equal(TextDecorations.Strikethrough, plain.Decoration);
        Assert.Equal(0.55, plain.Opacity);
        Assert.True(plain.HasSteps);
        Assert.Equal(app.Texts.Format("StepsShort", 1, 3), plain.StepsCaption);

        // Seleccion: avisa solo cuando cambia.
        var changed = new List<string?>();
        plain.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        plain.Selecting = true;
        plain.Selecting = true;
        plain.IsSelected = true;
        plain.IsSelected = true;
        Assert.Equal([nameof(TaskRow.Selecting), nameof(TaskRow.IsSelected)], changed);

        var step = new StepRow(new TaskStep { Title = "Paso", IsDone = true });
        Assert.Equal("Paso", step.Title);
        Assert.True(step.IsDone);
        Assert.Equal("ic_circle_check.png", step.StateIcon);
        Assert.Equal(TextDecorations.Strikethrough, step.Decoration);
        Assert.Equal(0.55, step.Opacity);
        Assert.Equal(step.Step.Id, step.Id);
        var open = new StepRow(new TaskStep { Title = "Otro" });
        Assert.Equal("ic_circle.png", open.StateIcon);
        Assert.Equal(TextDecorations.None, open.Decoration);
        Assert.Equal(1.0, open.Opacity);
    });

    // -----------------------------------------------------------------------
    // Celebracion
    // -----------------------------------------------------------------------

    [Fact]
    public void La_celebracion_tira_confeti_vibra_y_dice_los_puntos() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var page = new ContentPage();
        var view = new CelebrationView();
        page.Content = view;
        TestHandler.Attach(page);
        var label = view.Field<Label>("_xpLabel");

        view.Celebrate(new Celebration(10, 1.0, 100, 2, false, null));
        await UiThread.IdleAsync();
        Assert.Equal("+10 XP", label.Text);
        Assert.Equal([false], app.Ui.Haptics);

        view.Celebrate(new Celebration(15, 1.5, 115, 2, false, null));
        await UiThread.IdleAsync();
        Assert.Equal($"+15 XP · ¡Racha x{1.5:0.#}!", label.Text);

        view.HapticsEnabled = false;
        view.Celebrate(new Celebration(20, 1.0, 200, 3, true, null));
        await UiThread.IdleAsync();
        Assert.Equal("¡Nivel 3! +20 XP", label.Text);
        Assert.Equal(2, app.Ui.Haptics.Count);

        // El confeti cae fotograma a fotograma hasta que no queda nada; mientras, se dibuja.
        var timer = TestDispatcher.Instance.Timers.Last();
        Assert.True(timer.IsRunning);
        var drawable = (IDrawable)view.Field<GraphicsView>("_canvas").Drawable;
        drawable.Draw(new PictureCanvas(0, 0, 360, 640), new RectF(0, 0, 360, 640));
        for (var i = 0; i < 1000 && timer.IsRunning; i++)
        {
            timer.Fire();
        }

        Assert.False(timer.IsRunning);
    });
}
