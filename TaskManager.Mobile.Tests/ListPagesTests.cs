using System.Collections.ObjectModel;
using System.Globalization;
using TaskManager.Core.Models;
using TaskManager.Mobile.Models;
using TaskManager.Mobile.Pages;
using TaskManager.Mobile.Tests.Infra;

namespace TaskManager.Mobile.Tests;

/// <summary>Listas, detalle de una lista, tablero y calendario.</summary>
public class ListPagesTests
{
    // -----------------------------------------------------------------------
    // Mis listas
    // -----------------------------------------------------------------------

    [Fact]
    public void Mis_listas_cuenta_lo_pendiente_crea_abre_y_borra() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (casa, tasks) = await app.SeedAsync("Casa", "Pan", "Leche");
        await app.Tasks.CompleteTaskAsync(tasks[1]);
        var (hechas, done) = await app.SeedAsync("Hechas", "Fruta");
        await app.Tasks.CompleteTaskAsync(done[0]);
        await app.Repository.CreateListAsync("Vacia");

        var page = new ListsPage();
        await page.Appear();
        var rows = ((List<ListRow>)page.Named<CollectionView>("ListsView").ItemsSource).ToDictionary(r => r.Name, r => r.Caption);
        Assert.Equal(app.Texts.Format("ListPending", 1, 2), rows["Casa"]);
        Assert.Equal(app.Texts.Format("ListAllDone", 1), rows["Hechas"]);
        Assert.Equal(app.Texts["ListEmpty"], rows["Vacia"]);

        // Nueva: cancelar no crea; con nombre si.
        var count = ((List<ListRow>)page.Named<CollectionView>("ListsView").ItemsSource).Count;
        await page.Handler("OnNewListClicked");
        app.Ui.Answer("Trabajo");
        await page.Handler("OnNewListClicked");
        Assert.Contains("Trabajo", (await app.Repository.GetPrivateListsAsync()).Select(l => l.Name));
        Assert.Equal(count + 1, ((List<ListRow>)page.Named<CollectionView>("ListsView").ItemsSource).Count);

        // Abrir.
        await page.Handler("OnListTapped", null, new TappedEventArgs(casa.Id));
        await page.Handler("OnListTapped", null, new TappedEventArgs("x"));
        Assert.Equal($"{nameof(ListDetailPage)}?listId={casa.Id}", app.Ui.Routes.Single());

        // Borrar: siempre se pregunta.
        var dialogs = app.Ui.Dialogs.Count;
        await page.Handler("OnDeleteListClicked", new Button());
        await page.Handler("OnDeleteListClicked", PageDriver.RowButton(Guid.NewGuid()));
        Assert.Equal(dialogs, app.Ui.Dialogs.Count);
        await page.Handler("OnDeleteListClicked", PageDriver.RowButton(hechas.Id));
        Assert.Equal(app.Texts.Format("DeleteListMessage", "Hechas"), app.Ui.Dialogs.Last().Message);
        Assert.NotNull(await app.Repository.GetListAsync(hechas.Id));
        app.Ui.Answer(true);
        await page.Handler("OnDeleteListClicked", PageDriver.RowButton(hechas.Id));
        Assert.DoesNotContain("Hechas", (await app.Repository.GetPrivateListsAsync()).Select(l => l.Name));

        await page.Handler("OnRefreshClicked");
        Assert.Equal(count, ((List<ListRow>)page.Named<CollectionView>("ListsView").ItemsSource).Count);
    });

    // -----------------------------------------------------------------------
    // Una lista
    // -----------------------------------------------------------------------

    private static ObservableCollection<TaskRow> Rows(ListDetailPage page) =>
        (ObservableCollection<TaskRow>)page.Named<CollectionView>("TasksView").ItemsSource;

    [Fact]
    public void Detalle_de_lista_con_pasos_busqueda_y_pie() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (list, tasks) = await app.SeedAsync("Casa", "Mudanza", "Regar");
        await app.Repository.AddStepsAsync(tasks[0].Id, ["Cajas", "Camion"]);

        var page = new ListDetailPage { ListId = Uri.EscapeDataString(list.Id.ToString()) };
        await page.Appear();

        Assert.Equal("Casa", page.Title);
        var mudanza = Rows(page).Single(r => r.Task.Title == "Mudanza");
        Assert.True(mudanza.ShowSteps);
        Assert.Equal(["Cajas", "Camion"], mudanza.Steps.Select(s => s.Title));
        Assert.Equal(ProgressCaption.Footer(2, await app.Repository.CountProgressAsync(list.Id), app.Texts),
            page.Named<Label>("FooterLabel").Text);

        await PageDriver.Set(() => page.Named<Entry>("SearchEntry").Text = "reg");
        Assert.Equal(["Regar"], Rows(page).Select(r => r.Task.Title));
        Assert.True(page.HandleBack());
        await UiThread.IdleAsync();
        Assert.Equal(2, Rows(page).Count);
        Assert.False(page.HandleBack());

        // Reordenar y refrescar.
        var before = Rows(page).Select(r => r.Id).ToList();
        Rows(page).Move(1, 0);
        await page.Handler("OnReorderCompleted");
        await page.Handler("OnRefreshClicked");
        Assert.Equal(before.AsEnumerable().Reverse(), Rows(page).Select(r => r.Id));
    });

    [Fact]
    public void Detalle_de_lista_sin_lista_o_con_una_que_no_existe() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();

        var none = new ListDetailPage { ListId = "x" };
        await none.Appear();
        Assert.Null(none.Named<CollectionView>("TasksView").ItemsSource);
        none.Named<Entry>("QuickAdd").Text = "Algo";
        await none.Handler("OnAddClicked");
        Assert.Empty(app.Ui.Routes);
        await none.Handler("OnRenameListClicked");
        Assert.Empty(app.Ui.Dialogs);

        var gone = new ListDetailPage { ListId = Guid.NewGuid().ToString() };
        await gone.Appear();
        Assert.Equal("Lista", gone.Title);
    });

    [Fact]
    public void Detalle_de_lista_añade_marca_pasos_mi_dia_y_renombra() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (list, tasks) = await app.SeedAsync("Casa", "Mudanza");
        var steps = await app.Repository.AddStepsAsync(tasks[0].Id, ["Cajas"]);
        var page = new ListDetailPage { ListId = list.Id.ToString() };
        await page.Appear();

        // Añadir: vacio no; con titulo abre el detalle.
        page.Named<Entry>("QuickAdd").Text = " ";
        await page.Handler("OnAddClicked");
        page.Named<Entry>("QuickAdd").Text = "Regar";
        await page.Handler("OnAddClicked");
        var regar = Rows(page).Single(r => r.Task.Title == "Regar").Task;
        Assert.Equal($"{nameof(TaskDetailPage)}?taskId={regar.Id}", app.Ui.Routes.Single());

        // Marcar hecha y deshacer.
        await page.Handler("OnToggleDoneClicked", PageDriver.RowButton(regar.Id));
        Assert.True((await app.Repository.GetTaskAsync(regar.Id))!.IsDone);
        await page.Handler("OnToggleDoneClicked", PageDriver.RowButton(regar.Id));
        Assert.False((await app.Repository.GetTaskAsync(regar.Id))!.IsDone);

        // El ultimo paso completa la tarea (y celebra).
        await page.Handler("OnToggleStepClicked", PageDriver.RowButton(steps[0].Id));
        Assert.True((await app.Repository.GetTaskAsync(tasks[0].Id))!.IsDone);
        await page.Handler("OnToggleStepClicked", PageDriver.RowButton(steps[0].Id));

        // Mi dia.
        await page.Handler("OnToggleMyDayClicked", PageDriver.RowButton(regar.Id));
        Assert.True(Rows(page).Single(r => r.Id == regar.Id).InMyDay);

        // Filas que ya no estan o botones sin parametro: nada.
        foreach (var handler in new[] { "OnToggleDoneClicked", "OnToggleStepClicked", "OnToggleMyDayClicked" })
        {
            await page.Handler(handler, PageDriver.RowButton(Guid.NewGuid()));
            await page.Handler(handler, new Button());
        }

        // Abrir una tarea.
        await page.Handler("OnTaskTapped", null, new TappedEventArgs(tasks[0].Id));
        await page.Handler("OnTaskTapped", null, new TappedEventArgs(null));
        Assert.Equal(2, app.Ui.Routes.Count);

        // Renombrar: cancelar, el mismo nombre, uno nuevo.
        await page.Handler("OnRenameListClicked");
        app.Ui.Answer(" Casa ");
        await page.Handler("OnRenameListClicked");
        Assert.Equal("Casa", app.Ui.Dialogs.Last().Options[0]);
        app.Ui.Answer("Hogar");
        await page.Handler("OnRenameListClicked");
        Assert.Equal("Hogar", page.Title);
        Assert.Equal("Hogar", (await app.Repository.GetListAsync(list.Id))!.Name);
    });

    // -----------------------------------------------------------------------
    // Tablero
    // -----------------------------------------------------------------------

    private static ObservableCollection<TaskRow> Column(KanbanPage page, string name) =>
        (ObservableCollection<TaskRow>)page.Named<CollectionView>(name).ItemsSource;

    [Fact]
    public void El_tablero_reparte_por_estado_y_filtra() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Por hacer", "En curso", "Hecha", "Otra por hacer");
        await app.Tasks.SetInProgressAsync(tasks[1], true);
        await app.Tasks.CompleteTaskAsync(tasks[2]);
        await app.Repository.AddTagAsync([tasks[0].Id], "casa");
        var group = await app.Repository.SaveGroupAsync(new TaskGroup { Name = "Familia", JoinCode = "F" });
        var shared = await app.Repository.CreateListAsync("Compra", group.Id);
        await app.Repository.AddTaskAsync(shared.Id, "Del grupo");

        var page = new KanbanPage();
        await page.Appear();

        Assert.Equal(["Del grupo", "Otra por hacer", "Por hacer"], Column(page, "TodoView").Select(r => r.Task.Title).Order());
        Assert.Equal(["En curso"], Column(page, "DoingView").Select(r => r.Task.Title));
        Assert.Equal(["Hecha"], Column(page, "DoneView").Select(r => r.Task.Title));
        Assert.Equal("3", page.Named<Label>("TodoCount").Text);
        Assert.Equal("1", page.Named<Label>("DoingCount").Text);
        Assert.Equal("1", page.Named<Label>("DoneCount").Text);
        Assert.Equal(ProgressCaption.Footer(4, await app.Repository.CountProgressAsync(), app.Texts),
            page.Named<Label>("FooterLabel").Text);
        Assert.Equal("Familia · Compra", page.Field<Dictionary<Guid, string>>("_listNames")[shared.Id]);

        // Filtro «hechas».
        var chips = page.Named<HorizontalStackLayout>("FilterBox").OfType<Button>().ToList();
        await chips.Single(c => c.ClassId == nameof(TaskFilter.Done)).Click();
        Assert.Empty(Column(page, "TodoView"));
        Assert.Single(Column(page, "DoneView"));
        await chips.Single(c => c.ClassId == nameof(TaskFilter.All)).Click();

        // Etiqueta.
        var tagChips = page.Named<HorizontalStackLayout>("TagFilterBox").OfType<Button>().ToList();
        Assert.Equal(3, tagChips.Count);
        await tagChips[2].Click();
        Assert.Equal(["Por hacer"], Column(page, "TodoView").Select(r => r.Task.Title));
        Assert.Empty(Column(page, "DoingView"));
        await page.Named<HorizontalStackLayout>("TagFilterBox").OfType<Button>().ElementAt(1).Click();
        Assert.Equal(["Del grupo", "Otra por hacer"], Column(page, "TodoView").Select(r => r.Task.Title).Order());

        // La etiqueta activa desaparece: se suelta.
        page.SetField("_activeTag", "borrada");
        await page.Call("ReloadAsync");
        Assert.Null(page.Field<string?>("_activeTag"));

        // Busqueda y atras.
        await PageDriver.Set(() => page.Named<Entry>("SearchEntry").Text = "curso");
        Assert.Single(Column(page, "DoingView"));
        Assert.Empty(Column(page, "TodoView"));
        Assert.True(page.HandleBack());
        await UiThread.IdleAsync();
        Assert.False(page.HandleBack());
    });

    [Fact]
    public void El_tablero_sin_etiquetas_crea_abre_reordena_y_refresca() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        app.Application!.UserAppTheme = AppTheme.Dark;
        var page = new KanbanPage();
        await page.Appear();
        Assert.False(page.Named<VisualElement>("TagFilterScroll").IsVisible);

        page.Named<Entry>("QuickAdd").Text = "";
        await page.Handler("OnAddClicked");
        Assert.Empty(app.Ui.Routes);
        page.Named<Entry>("QuickAdd").Text = "Primera";
        await page.Handler("OnAddClicked");
        page.Named<Entry>("QuickAdd").Text = "Segunda";
        await page.Handler("OnAddClicked");
        Assert.Equal(2, Column(page, "TodoView").Count);
        Assert.Equal(2, app.Ui.Routes.Count);

        await page.Handler("OnCardTapped", null, new TappedEventArgs(Guid.Empty));
        await page.Handler("OnCardTapped", null, new TappedEventArgs(Column(page, "TodoView")[0].Id));
        Assert.Equal(3, app.Ui.Routes.Count);

        var order = Column(page, "TodoView").Select(r => r.Id).Reverse().ToList();
        Column(page, "TodoView").Move(1, 0);
        await page.Handler("OnTodoReordered");
        await page.Handler("OnDoingReordered");
        await page.Handler("OnDoneReordered");
        await page.Handler("OnRefreshClicked");
        Assert.Equal(order, Column(page, "TodoView").Select(r => r.Id));
    });

    // -----------------------------------------------------------------------
    // Calendario
    // -----------------------------------------------------------------------

    [Fact]
    public void El_calendario_pinta_el_mes_marca_los_dias_con_carga_y_enseña_el_elegido() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (list, _) = await app.SeedAsync("Casa");
        var hoy = await app.Repository.AddTaskAsync(list.Id, "Hoy", plannedFor: DateTime.Today);
        var group = await app.Repository.SaveGroupAsync(new TaskGroup { Name = "Familia", JoinCode = "F" });
        var shared = await app.Repository.CreateListAsync("Compra", group.Id);
        await app.Repository.AddTaskAsync(shared.Id, "Del grupo", plannedFor: DateTime.Today);

        var page = new CalendarPage();
        await page.Appear();

        var culture = CultureInfo.GetCultureInfo("es");
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var month = first.ToString("MMMM yyyy", culture);
        Assert.Equal(char.ToUpper(month[0], CultureInfo.InvariantCulture) + month[1..], page.Named<Label>("MonthLabel").Text);

        // Semana en español: empieza en lunes.
        var header = page.Named<Grid>("WeekdayRow").Children.OfType<Label>().Select(l => l.Text).ToList();
        Assert.Equal(7, header.Count);
        Assert.StartsWith("L", header[0]);

        var cells = page.Named<Grid>("MonthGrid").Children.OfType<Border>().ToList();
        Assert.Equal(DateTime.DaysInMonth(first.Year, first.Month), cells.Count);
        var today = cells[DateTime.Today.Day - 1];
        Assert.Equal(1.5, today.StrokeThickness);
        Assert.True(((VerticalStackLayout)today.Content!).Children.OfType<BoxView>().Single().IsVisible);

        var rows = (List<TaskRow>)page.Named<CollectionView>("DayTasksView").ItemsSource;
        Assert.Equal(["Del grupo", "Hoy"], rows.Select(r => r.Task.Title).Order());
        Assert.Contains("Familia · Compra", rows.Select(r => r.ListName));

        // Tocar una tarea la abre.
        await page.Handler("OnTaskTapped", new Label { BindingContext = rows.Single(r => r.Task.Title == "Hoy") }, new TappedEventArgs(null));
        await page.Handler("OnTaskTapped", new Label(), new TappedEventArgs(null));
        Assert.Equal($"{nameof(TaskDetailPage)}?taskId={hoy.Id}", app.Ui.Routes.Single());

        // Tocar otro dia lo elige.
        var otherDay = DateTime.Today.Day == 1 ? 2 : 1;
        await page.Named<Grid>("MonthGrid").Children.OfType<Border>().ElementAt(otherDay - 1).Tap();
        Assert.Empty((List<TaskRow>)page.Named<CollectionView>("DayTasksView").ItemsSource);
        Assert.Equal(new DateTime(first.Year, first.Month, otherDay), page.Field<DateTime>("_selected"));
    });

    [Fact]
    public void El_calendario_cambia_de_mes_y_crea_en_el_dia_elegido() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync(language: "en");
        var page = new CalendarPage();
        await page.Appear();

        await page.Handler("OnNextMonthClicked");
        var next = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1);
        Assert.Equal(next, page.Field<DateTime>("_selected"));
        Assert.Equal(next.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en")), page.Named<Label>("MonthLabel").Text);

        // En ingles la semana empieza en domingo.
        Assert.StartsWith("S", page.Named<Grid>("WeekdayRow").Children.OfType<Label>().First().Text);

        await page.Handler("OnPreviousMonthClicked");
        await page.Handler("OnPreviousMonthClicked");
        Assert.Equal(next.AddMonths(-2), page.Field<DateTime>("_selected"));

        page.Named<Entry>("QuickAdd").Text = " ";
        await page.Handler("OnAddClicked");
        page.Named<Entry>("QuickAdd").Text = "Cita";
        await page.Handler("OnAddClicked");
        var task = (await app.Repository.GetAllTasksAsync(TaskFilter.All)).Single();
        Assert.Equal(next.AddMonths(-2), task.PlannedFor);
        Assert.Equal($"{nameof(TaskDetailPage)}?taskId={task.Id}", app.Ui.Routes.Single());

        await page.Handler("OnRefreshClicked");
        Assert.Single((List<TaskRow>)page.Named<CollectionView>("DayTasksView").ItemsSource);
    });
}
