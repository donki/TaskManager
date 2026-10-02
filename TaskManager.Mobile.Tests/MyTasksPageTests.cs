using System.Collections.ObjectModel;
using TaskManager.Core.Data;
using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Models;
using TaskManager.Mobile.Pages;
using TaskManager.Mobile.Tests.Infra;

namespace TaskManager.Mobile.Tests;

/// <summary>«Mis tareas»: filtros, etiquetas, busqueda, seleccion multiple y captura rapida.</summary>
public class MyTasksPageTests
{
    private static async Task<MyTasksPage> OpenAsync(TestApp app)
    {
        // Version ya vista y permiso ya pedido: la pantalla no se va a Novedades ni pregunta.
        await app.Settings.MarkVersionSeenAsync(app.Ui.VersionString);
        await app.Settings.SetBoolAsync("notify.asked", true);
        var page = new MyTasksPage();
        await page.Appear();
        return page;
    }

    private static ObservableCollection<TaskRow> Rows(MyTasksPage page) =>
        (ObservableCollection<TaskRow>)page.Named<CollectionView>("TasksView").ItemsSource;

    [Fact]
    public void Enseña_lo_pendiente_con_su_lista_y_cuenta() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Comprar pan", "Regar", "Hecha");
        await app.Tasks.CompleteTaskAsync(tasks[2]);

        var page = await OpenAsync(app);

        Assert.Equal(["Comprar pan", "Regar"], Rows(page).Select(r => r.Task.Title).Order());
        Assert.All(Rows(page), r => Assert.Equal("Casa", r.ListName));
        Assert.Equal(app.Texts.Format("TaskCount", 2), page.Named<Label>("SummaryLabel").Text);
        Assert.Equal(app.Texts["FilterPending"], page.Named<Label>("FilterLabel").Text);
        Assert.Equal(ProgressCaption.Footer(2, await app.Repository.CountProgressAsync(), app.Texts),
            page.Named<Label>("FooterLabel").Text);
        Assert.False(page.Named<VisualElement>("SelectionBar").IsVisible);
        Assert.False(page.HandleBack());   // nada abierto: el Shell oculta la aplicacion

        // Una sola: en singular.
        await app.Tasks.CompleteTaskAsync(tasks[1]);
        await page.Call("ReloadAsync");
        Assert.Equal(app.Texts["TaskCountOne"], page.Named<Label>("SummaryLabel").Text);
    });

    [Fact]
    public void La_primera_vez_de_una_version_va_a_Novedades() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var page = new MyTasksPage();
        await page.Appear();

        Assert.Equal(["//WhatsNewPage"], app.Ui.Routes);
        Assert.False(app.Settings.HasUnseenVersion(app.Ui.VersionString));
        Assert.Equal(0, app.Reminders.Requests);   // el permiso se pide a la vuelta, no encima
    });

    [Fact]
    public void Pide_el_permiso_de_avisos_una_sola_vez() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        await app.Settings.MarkVersionSeenAsync(app.Ui.VersionString);
        var page = new MyTasksPage();

        await page.Appear();
        Assert.Equal(1, app.Reminders.Requests);
        Assert.True(app.Settings.GetBool("notify.asked", false));

        await page.Call("AskForNotificationsOnceAsync");
        Assert.Equal(1, app.Reminders.Requests);

        // Ya concedido: no se vuelve a pedir; con los avisos apagados, tampoco.
        await app.Settings.SetBoolAsync("notify.asked", false);
        app.Reminders.Allowed = true;
        await page.Call("AskForNotificationsOnceAsync");
        Assert.Equal(1, app.Reminders.Requests);

        await app.Settings.SetBoolAsync("notify.asked", false);
        await app.Settings.SetBoolAsync(SettingsService.KeyNotifyEnabled, false);
        await page.Call("AskForNotificationsOnceAsync");
        Assert.False(app.Settings.GetBool("notify.asked", false));

        // Si el sistema falla al preguntar, la pantalla sigue (y no se vuelve a intentar).
        await app.Settings.SetBoolAsync(SettingsService.KeyNotifyEnabled, true);
        app.Reminders.Fails = new InvalidOperationException("sin servicio");
        await page.Call("AskForNotificationsOnceAsync");
        Assert.True(app.Settings.GetBool("notify.asked", false));
        Assert.Equal(1, app.Reminders.Requests);
    });

    [Fact]
    public void Los_filtros_cambian_lo_que_se_ve_y_se_recuerdan() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Pendiente", "Anclada", "Hecha");
        await app.Repository.SetPinnedAsync([tasks[1].Id], true);
        await app.Tasks.CompleteTaskAsync(tasks[2]);
        var page = await OpenAsync(app);

        var chips = page.Named<HorizontalStackLayout>("FilterBox").OfType<Button>().ToList();
        Assert.Equal(TaskFilters.All.Length, chips.Count);

        await chips.Single(c => c.ClassId == nameof(TaskFilter.Done)).Click();
        Assert.Equal(["Hecha"], Rows(page).Select(r => r.Task.Title));
        Assert.Equal(TaskFilter.Done, app.Settings.TaskFilter);
        Assert.Equal(Colors.White, chips.Single(c => c.ClassId == nameof(TaskFilter.Done)).TextColor);
        Assert.NotEqual(Colors.White, chips.Single(c => c.ClassId == nameof(TaskFilter.Pending)).TextColor);

        await chips.Single(c => c.ClassId == nameof(TaskFilter.Pinned)).Click();
        Assert.Equal(["Anclada"], Rows(page).Select(r => r.Task.Title));
        Assert.StartsWith("📌", Rows(page)[0].Title);

        // Otra pagina nueva arranca donde se dejo.
        var again = await OpenAsync(app);
        Assert.Equal(app.Texts["FilterPinned"], again.Named<Label>("FilterLabel").Text);
    });

    [Fact]
    public void Etiquetas_filtran_y_se_pueden_borrar() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Pan", "Fruta", "Sin etiqueta", "Hecha");
        await app.Repository.AddTagAsync([tasks[0].Id, tasks[1].Id], "compra");
        await app.Tasks.CompleteTaskAsync(tasks[3]);
        await app.Repository.AddTagAsync([tasks[3].Id], "vieja");
        app.Application!.UserAppTheme = AppTheme.Dark;
        var page = await OpenAsync(app);

        Assert.True(page.Named<VisualElement>("TagFilterRow").IsVisible);
        var tagChips = page.Named<HorizontalStackLayout>("TagFilterBox").OfType<Button>().ToList();
        Assert.Equal([app.Texts["AllTags"], app.Texts["NoTagFilter"], "#compra"], tagChips.Select(b => b.Text));

        await tagChips[2].Click();
        Assert.Equal(["Fruta", "Pan"], Rows(page).Select(r => r.Task.Title).Order());
        Assert.Equal("compra", app.Settings.TaskTag);
        Assert.EndsWith("#compra", page.Named<Label>("FilterLabel").Text);

        await page.Named<HorizontalStackLayout>("TagFilterBox").OfType<Button>().ElementAt(1).Click();
        Assert.Equal(["Sin etiqueta"], Rows(page).Select(r => r.Task.Title));
        Assert.EndsWith(app.Texts["NoTagFilter"], page.Named<Label>("FilterLabel").Text);

        // Borrar la etiqueta (la pulsacion larga del chip): con pendientes, se pregunta con cuantas.
        await page.Named<HorizontalStackLayout>("TagFilterBox").OfType<Button>().ElementAt(2).Click();
        await page.Call("DeleteTagAsync", "compra");
        Assert.Equal(app.Texts.Format("DeleteTagPending", "compra", 2, 2), app.Ui.Dialogs.Last().Message);
        Assert.Equal(2, (await app.Repository.CountTagAsync("compra")).Total);

        app.Ui.Answer(true);
        await page.Call("DeleteTagAsync", "compra");
        Assert.Equal(0, (await app.Repository.CountTagAsync("compra")).Total);
        Assert.Null(app.Settings.TaskTag);
        Assert.Equal(app.Texts.Format("TagDeleted", "compra", 2), app.Ui.Dialogs.Last().Message);

        // Solo la llevan tareas hechas: no hay chips, pero la fila sigue para llegar a borrarla.
        Assert.Empty(page.Named<HorizontalStackLayout>("TagFilterBox").Children);
        Assert.True(page.Named<VisualElement>("TagFilterRow").IsVisible);
        app.Ui.Answer(true);
        await page.Call("DeleteTagAsync", "vieja");
        Assert.Equal(app.Texts.Format("DeleteTagDone", "vieja", 1), app.Ui.Dialogs[^2].Message);
        Assert.False(page.Named<VisualElement>("TagFilterRow").IsVisible);
    });

    [Fact]
    public void Una_etiqueta_activa_que_desaparece_se_suelta() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Pan", "Otra");
        await app.Repository.AddTagAsync([tasks[0].Id], "compra");
        await app.Repository.AddTagAsync([tasks[1].Id], "casa");
        await app.Settings.SetTaskTagAsync("borrada");
        var page = await OpenAsync(app);

        Assert.Null(page.Field<string?>("_activeTag"));
        Assert.Equal(2, Rows(page).Count);
    });

    [Fact]
    public void Buscar_y_vaciar_el_buscador_con_atras() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        await app.SeedAsync("Casa", "Comprar pan", "Regar");
        var page = await OpenAsync(app);

        await PageDriver.Set(() => page.Named<Entry>("SearchEntry").Text = "pan");
        Assert.Equal(["Comprar pan"], Rows(page).Select(r => r.Task.Title));

        Assert.True(page.HandleBack());
        await UiThread.IdleAsync();
        Assert.Equal(2, Rows(page).Count);
        Assert.False(page.HandleBack());
    });

    [Fact]
    public void Captura_rapida_crea_en_la_primera_lista_y_abre_el_detalle() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var page = await OpenAsync(app);

        page.Named<Entry>("QuickAdd").Text = "   ";
        await page.Handler("OnAddClicked");
        Assert.Empty(app.Ui.Routes);

        // Sin listas: se crea la de por defecto.
        page.Named<Entry>("QuickAdd").Text = " Llamar al banco ";
        await page.Handler("OnAddClicked");
        var task = (await app.Repository.GetAllTasksAsync(TaskFilter.All)).Single();
        Assert.Equal("Llamar al banco", task.Title);
        Assert.Equal(app.Texts["DefaultListName"], (await app.Repository.GetListAsync(task.ListId))!.Name);
        Assert.Equal($"{nameof(TaskDetailPage)}?taskId={task.Id}", app.Ui.Routes.Single());
        Assert.Equal(string.Empty, page.Named<Entry>("QuickAdd").Text);
    });

    [Fact]
    public void Tocar_una_fila_abre_el_detalle_y_marcarla_la_completa() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Comprar pan");
        var page = await OpenAsync(app);

        await page.Handler("OnTaskTapped", null, new TappedEventArgs("no es un id"));
        Assert.Empty(app.Ui.Routes);
        await page.Handler("OnTaskTapped", null, new TappedEventArgs(tasks[0].Id));
        Assert.Equal($"{nameof(TaskDetailPage)}?taskId={tasks[0].Id}", app.Ui.Routes.Single());

        await page.Handler("OnToggleDoneClicked", PageDriver.RowButton(tasks[0].Id));
        Assert.True((await app.Repository.GetTaskAsync(tasks[0].Id))!.IsDone);
        Assert.Empty(Rows(page));   // el filtro es «pendientes»

        await page.Handler("OnToggleDoneClicked", PageDriver.RowButton(tasks[0].Id));
        Assert.False((await app.Repository.GetTaskAsync(tasks[0].Id))!.IsDone);

        await page.Handler("OnToggleDoneClicked", PageDriver.RowButton(Guid.NewGuid()));
        await page.Handler("OnToggleDoneClicked", new Button());
    });

    [Fact]
    public void Reordenar_guarda_el_orden_que_se_ve() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        await app.SeedAsync("Casa", "A", "B", "C");
        var page = await OpenAsync(app);

        var before = Rows(page).Select(r => r.Task.Title).ToList();
        Rows(page).Move(2, 0);
        await page.Handler("OnReorderCompleted");

        var again = await OpenAsync(app);
        Assert.Equal(Rows(page).Select(r => r.Task.Title), Rows(again).Select(r => r.Task.Title));
        Assert.NotEqual(before, Rows(again).Select(r => r.Task.Title));
    });

    [Fact]
    public void Refrescar_sincroniza_y_repinta_y_siempre_para_la_rueda() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var page = await OpenAsync(app);
        await app.SeedAsync("Casa", "Nueva");

        page.Named<RefreshView>("Refresher").IsRefreshing = true;
        await UiThread.IdleAsync();
        Assert.False(page.Named<RefreshView>("Refresher").IsRefreshing);
        Assert.Single(Rows(page));

        await app.SeedAsync("Otra", "Otra mas");
        await page.Handler("OnRefreshClicked");
        Assert.Equal(2, Rows(page).Count);
        Assert.False(page.Named<VisualElement>("RefreshingBadge").IsVisible);
    });

    [Fact]
    public void Seleccion_multiple_marca_hace_ancla_etiqueta_mueve_y_borra() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (casa, tasks) = await app.SeedAsync("Casa", "A", "B", "C");
        var trabajo = await app.Repository.CreateListAsync("Trabajo");
        await app.Settings.SetTaskFilterAsync(TaskFilter.All);
        var page = await OpenAsync(app);

        // Sin nada marcado, los botones no hacen nada.
        foreach (var handler in new[] { "OnBulkDoneClicked", "OnBulkPendingClicked", "OnBulkPinClicked", "OnBulkTagClicked", "OnBulkMoveClicked", "OnBulkDeleteClicked" })
        {
            await page.Handler(handler);
        }

        Assert.Empty(app.Ui.Dialogs);

        await page.Handler("OnSelectModeClicked");
        Assert.True(page.Named<VisualElement>("SelectionBar").IsVisible);
        Assert.All(Rows(page), r => Assert.True(r.Selecting));
        Assert.Equal("ic_close.png", ((FileImageSource)page.Named<ImageButton>("SelectModeButton").Source).File);

        // Tocar filas marca en vez de abrir.
        await page.Handler("OnTaskTapped", null, new TappedEventArgs(tasks[0].Id));
        await page.Handler("OnTaskTapped", null, new TappedEventArgs(tasks[1].Id));
        await page.Handler("OnTaskTapped", null, new TappedEventArgs(Guid.NewGuid()));
        await page.Handler("OnRowCheckChanged", null, new CheckedChangedEventArgs(true));
        Assert.Empty(app.Ui.Routes);
        Assert.Equal(app.Texts.Format("SelectionCount", 2), page.Named<Label>("SelectionLabel").Text);

        // Anclar: si no lo son todas, las ancla; si ya lo son, las desancla.
        await page.Handler("OnBulkPinClicked");
        Assert.True((await app.Repository.GetTaskAsync(tasks[0].Id))!.IsPinned);
        Remark(page, tasks[0].Id, tasks[1].Id);
        await page.Handler("OnBulkPinClicked");
        Assert.False((await app.Repository.GetTaskAsync(tasks[1].Id))!.IsPinned);

        // Hechas y otra vez pendientes.
        Remark(page, tasks[0].Id, tasks[1].Id);
        await page.Handler("OnBulkDoneClicked");
        Assert.True((await app.Repository.GetTaskAsync(tasks[1].Id))!.IsDone);
        Remark(page, tasks[0].Id, tasks[1].Id);
        await page.Handler("OnBulkPendingClicked");
        Assert.False((await app.Repository.GetTaskAsync(tasks[1].Id))!.IsDone);

        // Etiqueta: cancelar; una que ya existe; una nueva escrita; nueva pero vacia.
        Remark(page, tasks[0].Id);
        await page.Handler("OnBulkTagClicked");
        app.Ui.Answer(app.Texts["BulkTagHint"], "urgente");
        await page.Handler("OnBulkTagClicked");
        Assert.Equal(["urgente"], TaskTags.Split((await app.Repository.GetTaskAsync(tasks[0].Id))!.Tags));
        Remark(page, tasks[1].Id);
        app.Ui.Answer("urgente");
        await page.Handler("OnBulkTagClicked");
        Assert.Equal(["urgente"], TaskTags.Split((await app.Repository.GetTaskAsync(tasks[1].Id))!.Tags));
        Remark(page, tasks[2].Id);
        app.Ui.Answer(app.Texts["BulkTagHint"], " ");
        await page.Handler("OnBulkTagClicked");
        Assert.Empty(TaskTags.Split((await app.Repository.GetTaskAsync(tasks[2].Id))!.Tags));

        // Mover: cancelar no mueve; elegir lista si.
        Remark(page, tasks[2].Id);
        await page.Handler("OnBulkMoveClicked");
        Assert.Equal(casa.Id, (await app.Repository.GetTaskAsync(tasks[2].Id))!.ListId);
        app.Ui.Answer("Trabajo");
        await page.Handler("OnBulkMoveClicked");
        Assert.Equal(trabajo.Id, (await app.Repository.GetTaskAsync(tasks[2].Id))!.ListId);

        // Borrar: cancelar; confirmar.
        Remark(page, tasks[2].Id);
        await page.Handler("OnBulkDeleteClicked");
        Assert.NotNull(await app.Repository.GetTaskAsync(tasks[2].Id));
        app.Ui.Answer(true);
        await page.Handler("OnBulkDeleteClicked");
        Assert.Null(await app.Repository.GetTaskAsync(tasks[2].Id));

        // Atras sale del modo de marcar.
        Assert.True(page.HandleBack());
        Assert.False(page.Named<VisualElement>("SelectionBar").IsVisible);
        Assert.All(Rows(page), r => Assert.False(r.IsSelected));
    });

    [Fact]
    public void Borrar_varias_con_series_pregunta_si_llevarse_las_series() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Regar", "Suelta");
        var serie = tasks[0];
        serie.PlannedFor = DateTime.Today;
        serie.DueAt = DateTime.Today.AddDays(2);
        serie.RecurrenceRule = new Recurrence(RecurrenceKind.Daily, 1).Serialize();
        await app.Repository.UpdateTaskAsync(serie);
        await app.Tasks.GenerateSeriesAsync(serie);
        await app.Settings.SetTaskFilterAsync(TaskFilter.All);
        var page = await OpenAsync(app);
        await page.Handler("OnSelectModeClicked");

        // Confirmar y luego cancelar la pregunta de la serie: nada.
        Remark(page, serie.Id);
        app.Ui.Answer(true, null);
        await page.Handler("OnBulkDeleteClicked");
        Assert.Equal(3, (await app.Repository.GetSeriesAsync(serie.SeriesId!.Value)).Count);

        // Solo las marcadas.
        Remark(page, serie.Id);
        app.Ui.Answer(true, app.Texts["OnlySelected"]);
        await page.Handler("OnBulkDeleteClicked");
        Assert.Equal(2, (await app.Repository.GetSeriesAsync(serie.SeriesId!.Value)).Count);

        // La serie entera.
        var otra = (await app.Repository.GetSeriesAsync(serie.SeriesId!.Value))[0];
        Remark(page, otra.Id);
        app.Ui.Answer(true, app.Texts["WholeSeries"]);
        await page.Handler("OnBulkDeleteClicked");
        Assert.Empty(await app.Repository.GetSeriesAsync(serie.SeriesId!.Value));
        Assert.NotNull(await app.Repository.GetTaskAsync(tasks[1].Id));
    });

    [Fact]
    public void La_pagina_de_etiquetas_borra_y_avisa_al_volver() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Pan", "Hecha");
        await app.Repository.AddTagAsync([tasks[0].Id], "compra");
        await app.Tasks.CompleteTaskAsync(tasks[1]);
        await app.Repository.AddTagAsync([tasks[1].Id], "vieja");
        var page = await OpenAsync(app);

        await page.Handler("OnTagsClicked");
        var tags = Assert.IsType<TagsPage>(app.Ui.Pushed.Single());
        await tags.Call("ReloadAsync");

        var rows = tags.Field<VerticalStackLayout>("_rows").OfType<Grid>().ToList();
        Assert.Equal(2, rows.Count);
        var labels = rows.Select(r => ((Label)((VerticalStackLayout)r.Children[0]).Children[0]).Text).ToList();
        Assert.Equal(["#compra", "#vieja"], labels);

        // Cancelar no borra; confirmar si, y la pagina lo dice al irse.
        await ((ImageButton)rows[1].Children[1]).Click();
        Assert.False(tags.Changed);
        Assert.Equal(app.Texts.Format("DeleteTagDone", "vieja", 1), app.Ui.Dialogs.Last().Message);
        app.Ui.Answer(true);
        await ((ImageButton)rows[0].Children[1]).Click();
        Assert.Equal(app.Texts.Format("DeleteTagPending", "compra", 1, 1), app.Ui.Dialogs.Last().Message);
        Assert.True(tags.Changed);
        Assert.Single(tags.Field<VerticalStackLayout>("_rows").OfType<Grid>());

        await tags.Raise("Disappearing");
        Assert.Empty(page.Named<HorizontalStackLayout>("TagFilterBox").Children);

        // Sin etiquetas: el aviso de vacio.
        app.Ui.Answer(true);
        await ((ImageButton)tags.Field<VerticalStackLayout>("_rows").OfType<Grid>().Single().Children[1]).Click();
        Assert.True(tags.Field<Label>("_empty").IsVisible);
    });

    [Fact]
    public void Sin_ninguna_lista_crear_hace_la_de_por_defecto_y_mover_no_hace_nada() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (list, tasks) = await app.SeedAsync("Casa", "Suelta");
        await app.Settings.SetTaskFilterAsync(TaskFilter.All);
        var page = await OpenAsync(app);
        await page.Handler("OnSelectModeClicked");
        Remark(page, tasks[0].Id);

        foreach (var other in await app.Repository.GetPrivateListsAsync())
        {
            await app.Repository.DeleteListAsync(other);
        }

        // Sin listas a las que mover: ni se pregunta.
        await page.Handler("OnBulkMoveClicked");
        Assert.Empty(app.Ui.Dialogs);

        // Al repintar ya no hay lista por defecto: la captura rapida la crea.
        await page.Call("ReloadAsync");
        page.Named<Entry>("QuickAdd").Text = "Nueva";
        await page.Handler("OnAddClicked");
        var created = (await app.Repository.GetPrivateListsAsync()).Single();
        Assert.Equal(app.Texts["DefaultListName"], created.Name);
        Assert.NotEqual(list.Id, created.Id);
    });

    [Fact]
    public void Los_chips_de_etiqueta_se_preparan_para_la_pulsacion_larga() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Pan");
        await app.Repository.AddTagAsync([tasks[0].Id], "compra");
        var page = await OpenAsync(app);

        // Al recibir su control nativo, el chip de una etiqueta de verdad engancha la pulsacion larga
        // (en Android); los de «todas» y «sin etiqueta» no la llevan.
        foreach (var chip in page.Named<HorizontalStackLayout>("TagFilterBox").OfType<Button>())
        {
            TestHandler.Attach(chip);
            Assert.NotNull(chip.Handler);
        }
    });

    private static void Remark(MyTasksPage page, params Guid[] ids)
    {
        foreach (var row in Rows(page))
        {
            row.IsSelected = ids.Contains(row.Id);
        }
    }
}
