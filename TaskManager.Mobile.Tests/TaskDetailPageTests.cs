using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;
using TaskManager.Mobile.Models;
using TaskManager.Mobile.Pages;
using TaskManager.Mobile.Services;
using TaskManager.Mobile.Tests.Infra;

namespace TaskManager.Mobile.Tests;

/// <summary>El detalle de una tarea: cargar, editar, guardar, pasos, adjuntos, repeticion y borrar.</summary>
public class TaskDetailPageTests
{
    private static async Task<TaskDetailPage> OpenAsync(TestApp app, TaskItem task)
    {
        var page = new TaskDetailPage { TaskId = task.Id.ToString() };
        await page.Appear();
        return page;
    }

    private static async Task<TaskItem> Reload(TestApp app, TaskItem task) => (await app.Repository.GetTaskAsync(task.Id))!;

    [Fact]
    public void Carga_la_tarea_en_los_controles() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (casa, tasks) = await app.SeedAsync("Casa", "Comprar pan");
        var trabajo = await app.Repository.CreateListAsync("Trabajo");
        var task = tasks[0];
        task.Notes = "integral";
        task.Tags = TaskTags.FromInput("compra, casa");
        task.DueAt = DateTime.Today.AddDays(3);
        task.IsPinned = true;
        task.InProgress = true;
        task.RecurrenceRule = new Recurrence(RecurrenceKind.Weekly, 2, Recurrence.MaskOf([DayOfWeek.Monday])).Serialize();
        await app.Repository.UpdateTaskAsync(task);
        await app.Repository.AddStepsAsync(task.Id, ["uno", "dos"]);
        await app.Repository.AddLinkAsync(task.Id, "https://example.com");
        var otra = await app.Repository.AddTaskAsync(trabajo.Id, "Otra");
        otra.Tags = "oficina";
        await app.Repository.UpdateTaskAsync(otra);

        var page = await OpenAsync(app, task);

        Assert.Equal("Comprar pan", page.Title);
        Assert.Equal("Comprar pan", page.Named<Editor>("TitleEntry").Text);
        Assert.Equal("integral", page.Named<Editor>("NotesEditor").Text);
        Assert.Contains("compra", page.Named<Entry>("TagsEntry").Text);
        Assert.True(page.Named<Switch>("PinSwitch").IsToggled);
        Assert.True(page.Named<Switch>("ProgressSwitch").IsToggled);
        Assert.False(page.Named<Switch>("DoneSwitch").IsToggled);
        Assert.True(page.Named<Switch>("DueSwitch").IsToggled);
        Assert.True(page.Named<DatePicker>("DuePicker").IsVisible);
        Assert.Equal(DateTime.Today.AddDays(3), page.Named<DatePicker>("DuePicker").Date);
        Assert.False(page.Named<Switch>("PlannedSwitch").IsToggled);
        Assert.Equal(2, page.Named<Picker>("RecurrencePicker").SelectedIndex);
        Assert.Equal(2, page.Named<Stepper>("IntervalStepper").Value);
        Assert.Equal(7, page.Named<HorizontalStackLayout>("WeekdaysBox").Count);
        Assert.Equal(32, ((List<string>)page.Named<Picker>("MonthDayPicker").ItemsSource).Count);
        Assert.Equal(13, ((List<string>)page.Named<Picker>("MonthPicker").ItemsSource).Count);

        // Las listas, con la de la tarea marcada; las etiquetas que existen, como chips.
        var lists = (List<string>)page.Named<Picker>("ListPicker").ItemsSource;
        Assert.Equal(lists.IndexOf("Casa"), page.Named<Picker>("ListPicker").SelectedIndex);
        Assert.True(page.Named<FlexLayout>("KnownTagsBox").IsVisible);
        var chips = page.Named<FlexLayout>("KnownTagsBox").OfType<Button>().Select(b => b.Text).ToList();
        Assert.Equal(["#casa", "#compra", "#oficina"], chips.Order());

        Assert.Equal(2, ((System.Collections.ICollection)page.Named<CollectionView>("StepsView").ItemsSource).Count);
        Assert.False(page.Named<Label>("NoStepsLabel").IsVisible);
        Assert.Single(page.Named<VerticalStackLayout>("AttachmentsBox"));
        Assert.False(page.Named<Label>("NoAttachmentsLabel").IsVisible);

        // Recien cargada no hay cambios: atras no pregunta.
        Assert.False(page.HandleBack());
    });

    [Fact]
    public void Sin_identificador_no_carga_nada_y_si_la_tarea_no_existe_vuelve_atras() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();

        var vacia = new TaskDetailPage { TaskId = "no-es-un-guid" };
        await vacia.Appear();
        Assert.Empty(app.Ui.Routes);
        Assert.False(vacia.HandleBack());

        var borrada = new TaskDetailPage { TaskId = Uri.EscapeDataString(Guid.NewGuid().ToString()) };
        await borrada.Appear();
        Assert.Equal([".."], app.Ui.Routes);

        // Sin tarea, los botones no hacen nada.
        foreach (var handler in new[] { "OnAddStepClicked", "OnAddLinkClicked", "OnAddFileClicked", "OnPasteAttachmentClicked", "OnDeleteClicked" })
        {
            await borrada.Handler(handler);
        }

        await borrada.Handler("OnEditStepClicked", PageDriver.RowButton(Guid.NewGuid()));
        await borrada.Call("OnPinToggled", null, new ToggledEventArgs(true));
        await borrada.Call("OnProgressToggled", null, new ToggledEventArgs(true));
        await borrada.Call("OnDoneToggled", null, new ToggledEventArgs(true));
        Assert.False((bool)(await borrada.Call("SaveAsync", false))!);
        await borrada.Call("LoadListsAsync");
        await borrada.Call("LoadStepsAsync");
        await borrada.Call("LoadAttachmentsAsync");
        Assert.Empty(app.Ui.Dialogs);
    });

    [Fact]
    public void Guardar_escribe_lo_editado_en_una_linea_reprograma_el_aviso_y_vuelve() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Comprar pan");
        var trabajo = await app.Repository.CreateListAsync("Trabajo");
        var task = tasks[0];
        var page = await OpenAsync(app, task);

        page.Named<Editor>("TitleEntry").Text = "  Comprar\r\npan   y leche ";
        page.Named<Editor>("NotesEditor").Text = "  sin gluten ";
        page.Named<Entry>("TagsEntry").Text = "casa, compra";
        await PageDriver.Set(() => page.Named<Switch>("DueSwitch").IsToggled = true);
        Assert.True(page.Named<DatePicker>("DuePicker").IsVisible);
        page.Named<DatePicker>("DuePicker").Date = DateTime.Today.AddDays(5);
        await PageDriver.Set(() => page.Named<Switch>("PlannedSwitch").IsToggled = true);
        Assert.True(page.Named<DatePicker>("PlannedPicker").IsVisible);
        page.Named<DatePicker>("PlannedPicker").Date = DateTime.Today.AddDays(1);
        var lists = (List<string>)page.Named<Picker>("ListPicker").ItemsSource;
        page.Named<Picker>("ListPicker").SelectedIndex = lists.IndexOf("Trabajo");

        Assert.True(page.HandleBack());   // hay cambios: pregunta (sin respuesta = seguir aqui)
        await UiThread.IdleAsync();
        Assert.Equal("sheet", app.Ui.Dialogs.Single().Kind);
        Assert.Empty(app.Ui.Routes);

        await page.Handler("OnSaveClicked");

        var saved = await Reload(app, task);
        Assert.Equal("Comprar pan y leche", saved.Title);
        Assert.Equal("sin gluten", saved.Notes);
        Assert.Equal(["casa", "compra"], TaskTags.Split(saved.Tags));
        Assert.Equal(DateTime.Today.AddDays(5), saved.DueAt);
        Assert.Equal(DateTime.Today.AddDays(1), saved.PlannedFor);
        Assert.Equal(trabajo.Id, saved.ListId);
        Assert.Equal([".."], app.Ui.Routes);

        // El aviso, a las nueve del dia del plazo.
        var (alarm, moment) = app.Reminders.Scheduled[ReminderScheduler.RequestCodeFor(task.Id)];
        Assert.Equal(DateTime.Today.AddDays(5).AddHours(9), moment);
        Assert.Equal(task.Id, alarm.TaskId);

        // Quitar el plazo cancela el aviso.
        page = await OpenAsync(app, saved);
        await PageDriver.Set(() => page.Named<Switch>("DueSwitch").IsToggled = false);
        Assert.False(page.Named<DatePicker>("DuePicker").IsVisible);
        await PageDriver.Set(() => page.Named<Switch>("PlannedSwitch").IsToggled = false);
        await page.Handler("OnSaveClicked");
        Assert.Null((await Reload(app, task)).DueAt);
        Assert.DoesNotContain(ReminderScheduler.RequestCodeFor(task.Id), app.Reminders.Scheduled.Keys);
    });

    [Fact]
    public void Sin_titulo_no_guarda_y_lo_dice() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Comprar pan");
        var page = await OpenAsync(app, tasks[0]);

        page.Named<Editor>("TitleEntry").Text = " \n ";
        await page.Handler("OnSaveClicked");

        Assert.Equal(app.Texts["NeedTitleTitle"], app.Ui.Dialogs.Single().Title);
        Assert.Empty(app.Ui.Routes);
        Assert.Equal("Comprar pan", (await Reload(app, tasks[0])).Title);

        // En silencio (al salir con «guardar») no hay aviso, pero tampoco se guarda.
        Assert.False((bool)(await page.Call("SaveAsync", true))!);
        Assert.Single(app.Ui.Dialogs);
    });

    [Fact]
    public void Repetir_sin_fechas_no_se_puede_y_con_fechas_crea_la_serie() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Regar");
        var task = tasks[0];
        var page = await OpenAsync(app, task);

        await PageDriver.Set(() => page.Named<Picker>("RecurrencePicker").SelectedIndex = 1);   // diaria
        Assert.True(page.Named<Stepper>("IntervalStepper").IsVisible);
        Assert.True(page.Named<ScrollView>("WeekdaysScroll").IsVisible);
        Assert.False(page.Named<VisualElement>("MonthDayRow").IsVisible);
        Assert.Equal(new Recurrence(RecurrenceKind.Daily, 1).Describe(app.Texts), page.Named<Label>("RecurrenceLabel").Text);

        await page.Handler("OnSaveClicked");
        Assert.Equal(app.Texts["RecurrenceNeedsDates"], app.Ui.Dialogs.Single().Message);
        Assert.Equal(string.Empty, (await Reload(app, task)).RecurrenceRule);
        Assert.Empty(app.Ui.Routes);
        Assert.False((bool)(await page.Call("SaveAsync", true))!);
        Assert.Single(app.Ui.Dialogs);

        // Con las dos fechas, la serie se escribe y se cuenta.
        await PageDriver.Set(() => page.Named<Switch>("PlannedSwitch").IsToggled = true);
        page.Named<DatePicker>("PlannedPicker").Date = DateTime.Today.AddDays(1);
        await PageDriver.Set(() => page.Named<Switch>("DueSwitch").IsToggled = true);
        page.Named<DatePicker>("DuePicker").Date = DateTime.Today.AddDays(3);
        await page.Handler("OnSaveClicked");

        var saved = await Reload(app, task);
        Assert.NotNull(saved.SeriesId);
        Assert.Equal(3, (await app.Repository.GetSeriesAsync(saved.SeriesId!.Value)).Count);
        Assert.Equal(app.Texts.Format("SeriesCreated", 3), app.Ui.Dialogs.Last().Message);
        Assert.Equal([".."], app.Ui.Routes);

        // Mover la fecha de una vuelta no rehace la serie (la regla no cambia): no hay aviso nuevo.
        page = await OpenAsync(app, saved);
        page.Named<DatePicker>("DuePicker").Date = DateTime.Today.AddDays(2);
        await page.Handler("OnSaveClicked");
        Assert.Equal(2, app.Ui.Dialogs.Count);
        Assert.Equal(3, (await app.Repository.GetSeriesAsync(saved.SeriesId!.Value)).Count);
    });

    [Fact]
    public void La_repeticion_elige_dias_dia_del_mes_y_mes() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Pagar");
        var page = await OpenAsync(app, tasks[0]);

        // Semanal: los siete dias, lunes primero; tocar uno lo enciende y otra vez lo apaga.
        await PageDriver.Set(() => page.Named<Picker>("RecurrencePicker").SelectedIndex = 2);
        var lunes = page.Named<HorizontalStackLayout>("WeekdaysBox").OfType<Button>().First();
        await lunes.Click();
        Assert.Equal(Recurrence.MaskOf([DayOfWeek.Monday]), page.Field<byte>("_days"));
        var lunesOtraVez = page.Named<HorizontalStackLayout>("WeekdaysBox").OfType<Button>().First();
        Assert.Equal(Colors.White, lunesOtraVez.TextColor);
        await lunesOtraVez.Click();
        Assert.Equal(0, page.Field<byte>("_days"));

        // Mensual: el dia del mes; anual: tambien el mes.
        await PageDriver.Set(() => page.Named<Picker>("RecurrencePicker").SelectedIndex = 3);
        Assert.True(page.Named<VisualElement>("MonthDayRow").IsVisible);
        Assert.False(page.Named<VisualElement>("MonthRow").IsVisible);
        await PageDriver.Set(() => page.Named<Picker>("MonthDayPicker").SelectedIndex = 15);
        Assert.Equal(15, page.Field<byte>("_monthDay"));

        await PageDriver.Set(() => page.Named<Picker>("RecurrencePicker").SelectedIndex = 4);
        Assert.True(page.Named<VisualElement>("MonthRow").IsVisible);
        await PageDriver.Set(() => page.Named<Picker>("MonthPicker").SelectedIndex = 6);
        Assert.Equal(6, page.Field<byte>("_month"));
        Assert.Equal("Junio", ((List<string>)page.Named<Picker>("MonthPicker").ItemsSource)[6]);

        // El intervalo cambia la descripcion.
        await PageDriver.Set(() => page.Named<Stepper>("IntervalStepper").Value = 3);
        Assert.Equal(new Recurrence(RecurrenceKind.Yearly, 3).Describe(app.Texts), page.Named<Label>("RecurrenceLabel").Text);

        // Nunca: sin intervalo.
        await PageDriver.Set(() => page.Named<Picker>("RecurrencePicker").SelectedIndex = 0);
        Assert.False(page.Named<Stepper>("IntervalStepper").IsVisible);

        // Mientras carga, los cambios no tocan nada.
        page.SetField("_loading", true);
        await page.Call("OnRecurrenceChanged", null, EventArgs.Empty);
        await page.Call("OnIntervalChanged", null, new ValueChangedEventArgs(1, 2));
    });

    [Fact]
    public void Salir_con_cambios_pregunta_guardar_descartar_o_seguir() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Comprar pan");
        var task = tasks[0];

        // Sin cambios: vuelve sin preguntar.
        var page = await OpenAsync(app, task);
        await page.Call("LeaveAsync");
        Assert.Equal([".."], app.Ui.Routes);
        Assert.Empty(app.Ui.Dialogs);

        // Descartar: vuelve y no guarda.
        page = await OpenAsync(app, task);
        page.Named<Editor>("TitleEntry").Text = "Otra cosa";
        app.Ui.Answer(app.Texts["Discard"]);
        await page.Call("LeaveAsync");
        Assert.Equal(2, app.Ui.Routes.Count);
        Assert.Equal("Comprar pan", (await Reload(app, task)).Title);

        // Seguir aqui (tocar fuera): ni guarda ni vuelve.
        app.Ui.Answer(new object?[] { null });
        await page.Call("LeaveAsync");
        Assert.Equal(2, app.Ui.Routes.Count);

        // Guardar, pero sin titulo: no se va.
        page.Named<Editor>("TitleEntry").Text = "";
        app.Ui.Answer(app.Texts["Save"]);
        await page.Call("LeaveAsync");
        Assert.Equal(2, app.Ui.Routes.Count);

        // Guardar: guarda y vuelve. Tambien desde la flecha de la barra (BackButtonBehavior).
        page.Named<Editor>("TitleEntry").Text = "Comprar pan bueno";
        app.Ui.Answer(app.Texts["Save"]);
        var behavior = Shell.GetBackButtonBehavior(page);
        behavior.Command.Execute(null);
        await UiThread.IdleAsync();
        Assert.Equal(3, app.Ui.Routes.Count);
        Assert.Equal("Comprar pan bueno", (await Reload(app, task)).Title);

        // Mientras se esta saliendo, una segunda pulsacion no hace nada.
        page.SetField("_leaving", true);
        await page.Call("LeaveAsync");
        Assert.Equal(3, app.Ui.Routes.Count);
    });

    [Fact]
    public void Anclar_empezar_y_completar_se_guardan_al_tocarlos() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Comprar pan");
        var task = tasks[0];
        var page = await OpenAsync(app, task);

        await PageDriver.Set(() => page.Named<Switch>("PinSwitch").IsToggled = true);
        Assert.True((await Reload(app, task)).IsPinned);

        await PageDriver.Set(() => page.Named<Switch>("DoneSwitch").IsToggled = true);
        Assert.True((await Reload(app, task)).IsDone);
        Assert.NotEmpty(TestDispatcher.Instance.Timers);   // el confeti arranca

        // Empezada devuelve a pendientes y apaga «hecha».
        await PageDriver.Set(() => page.Named<Switch>("ProgressSwitch").IsToggled = true);
        var reloaded = await Reload(app, task);
        Assert.True(reloaded.InProgress);
        Assert.False(page.Named<Switch>("DoneSwitch").IsToggled);

        await PageDriver.Set(() => page.Named<Switch>("DoneSwitch").IsToggled = true);
        await PageDriver.Set(() => page.Named<Switch>("DoneSwitch").IsToggled = false);
        Assert.False((await Reload(app, task)).IsDone);

        // El mismo valor que ya tiene no reescribe nada.
        await page.Call("OnPinToggled", null, new ToggledEventArgs(true));
        await page.Call("OnProgressToggled", null, new ToggledEventArgs(page.Field<TaskItem>("_task").InProgress));
        await page.Call("OnDoneToggled", null, new ToggledEventArgs(false));
    });

    [Fact]
    public void Pasos_se_añaden_marcan_cambian_borran_y_reordenan() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Mudanza");
        var task = tasks[0];
        var page = await OpenAsync(app, task);
        Assert.True(page.Named<Label>("NoStepsLabel").IsVisible);

        page.Named<Entry>("NewStepEntry").Text = "   ";
        await page.Handler("OnAddStepClicked");
        Assert.Empty(await app.Repository.GetStepsAsync(task.Id));

        page.Named<Entry>("NewStepEntry").Text = " Cajas ";
        await page.Handler("OnAddStepClicked");
        page.Named<Entry>("NewStepEntry").Text = "Camion";
        await page.Handler("OnAddStepClicked");
        var steps = await app.Repository.GetStepsAsync(task.Id);
        Assert.Equal(["Cajas", "Camion"], steps.Select(s => s.Title));
        Assert.Equal(string.Empty, page.Named<Entry>("NewStepEntry").Text);

        // Marcar un paso.
        await page.Handler("OnToggleStepClicked", PageDriver.RowButton(steps[0].Id));
        Assert.True((await app.Repository.GetStepAsync(steps[0].Id))!.IsDone);

        // Cambiar el texto: cancelar no toca nada; escribir lo cambia.
        await page.Handler("OnEditStepClicked", PageDriver.RowButton(steps[1].Id));
        Assert.Equal("Camion", (await app.Repository.GetStepAsync(steps[1].Id))!.Title);
        app.Ui.Answer("Camion grande");
        await page.Handler("OnEditStepClicked", PageDriver.RowButton(steps[1].Id));
        Assert.Equal("Camion grande", (await app.Repository.GetStepAsync(steps[1].Id))!.Title);
        Assert.Equal("Camion", app.Ui.Dialogs.Last().Options[0]);

        // Reordenar: se guarda el orden que se ve.
        var rows = (System.Collections.ObjectModel.ObservableCollection<StepRow>)page.Named<CollectionView>("StepsView").ItemsSource;
        rows.Move(1, 0);
        await page.Handler("OnStepsReordered");
        Assert.Equal(["Camion grande", "Cajas"], (await app.Repository.GetStepsAsync(task.Id)).Select(s => s.Title));

        // Borrar.
        await page.Handler("OnDeleteStepClicked", PageDriver.RowButton(steps[0].Id));
        Assert.Single(await app.Repository.GetStepsAsync(task.Id));

        // Pasos que ya no existen o botones sin parametro: nada.
        foreach (var handler in new[] { "OnToggleStepClicked", "OnEditStepClicked", "OnDeleteStepClicked" })
        {
            await page.Handler(handler, PageDriver.RowButton(Guid.NewGuid()));
            await page.Handler(handler, new Button());
        }

        Assert.Single(await app.Repository.GetStepsAsync(task.Id));
    });

    [Fact]
    public void Completar_el_ultimo_paso_celebra() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Mudanza");
        var steps = await app.Repository.AddStepsAsync(tasks[0].Id, ["Unico"]);
        var page = await OpenAsync(app, tasks[0]);

        await page.Handler("OnToggleStepClicked", PageDriver.RowButton(steps[0].Id));

        Assert.True((await Reload(app, tasks[0])).IsDone);
        Assert.NotEmpty(TestDispatcher.Instance.Timers.Where(t => t.IsRunning));
    });

    [Fact]
    public void Las_etiquetas_conocidas_se_ponen_y_quitan_tocandolas() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Uno", "Dos");
        tasks[1].Tags = TaskTags.FromInput("casa, compra");
        await app.Repository.UpdateTaskAsync(tasks[1]);
        app.Application!.UserAppTheme = AppTheme.Dark;
        var page = await OpenAsync(app, tasks[0]);

        Chip("#casa").SendClicked();
        await UiThread.IdleAsync();
        Assert.Equal("casa", page.Named<Entry>("TagsEntry").Text);
        Assert.Equal(Colors.White, Chip("#casa").TextColor);

        Chip("#compra").SendClicked();
        await UiThread.IdleAsync();
        Assert.Equal("casa, compra", page.Named<Entry>("TagsEntry").Text);

        Chip("#CASA".ToLowerInvariant()).SendClicked();
        await UiThread.IdleAsync();
        Assert.Equal("compra", page.Named<Entry>("TagsEntry").Text);
        Assert.NotEqual(Colors.White, Chip("#casa").TextColor);

        Button Chip(string text) => page.Named<FlexLayout>("KnownTagsBox").OfType<Button>().Single(b => b.Text == text);
    });

    [Fact]
    public void Enlaces_y_ficheros_se_adjuntan_abren_y_quitan() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Factura");
        var task = tasks[0];
        var page = await OpenAsync(app, task);
        Assert.True(page.Named<Label>("NoAttachmentsLabel").IsVisible);

        // Enlace: cancelar no añade; escribirlo si.
        await page.Handler("OnAddLinkClicked");
        Assert.Empty(await app.Repository.GetAttachmentsAsync(task.Id));
        app.Ui.Answer("https://example.com/factura");
        await page.Handler("OnAddLinkClicked");

        // Fichero: cancelar el selector no hace nada; elegir uno lo mete dentro.
        await page.Handler("OnAddFileClicked");
        app.Ui.FileToPick = new PickedFile("factura.pdf", () => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
        await page.Handler("OnAddFileClicked");

        var items = await app.Repository.GetAttachmentsAsync(task.Id);
        Assert.Equal(2, items.Count);
        Assert.Equal([1, 2, 3], items.Single(i => !i.IsUrl).Data);
        Assert.Equal(2, page.Named<VerticalStackLayout>("AttachmentsBox").Count);

        // Demasiado grande: se dice y no se guarda.
        app.Ui.FileToPick = new PickedFile("enorme.bin",
            () => Task.FromResult<Stream>(new MemoryStream(new byte[TaskAttachment.MaxFileBytes + 1])));
        await page.Handler("OnAddFileClicked");
        Assert.Equal(app.Texts.Format("FileTooBig", 5), app.Ui.Dialogs.Last().Message);

        // Un fallo al leer se cuenta.
        app.Ui.FileToPick = new PickedFile("roto.bin", () => throw new IOException("disco roto"));
        await page.Handler("OnAddFileClicked");
        Assert.Equal("disco roto", app.Ui.Dialogs.Last().Message);
        Assert.Equal(2, (await app.Repository.GetAttachmentsAsync(task.Id)).Count);

        // Abrir: el enlace en el navegador, el fichero volcado a la cache y abierto.
        var rows = page.Named<VerticalStackLayout>("AttachmentsBox").OfType<Grid>().ToList();
        foreach (var row in rows)
        {
            await ((View)row.Children[1]).Tap();
        }

        Assert.Contains("browser:https://example.com/factura", app.Ui.Opened);
        var opened = app.Ui.Opened.Single(o => o.StartsWith("file:", StringComparison.Ordinal))[5..];
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(opened));

        // Si no se puede abrir, se dice.
        app.Ui.OpenFails = new InvalidOperationException("sin aplicacion");
        await ((View)rows[0].Children[1]).Tap();
        Assert.Equal("sin aplicacion", app.Ui.Dialogs.Last().Message);

        // Quitar.
        await ((ImageButton)rows[0].Children[2]).Click();
        Assert.Single(await app.Repository.GetAttachmentsAsync(task.Id));
    });

    [Fact]
    public void Pegar_una_imagen_del_portapapeles() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Captura");
        var task = tasks[0];
        var page = await OpenAsync(app, task);

        await page.Handler("OnPasteAttachmentClicked");
        Assert.Equal(app.Texts["PasteNothing"], app.Ui.Dialogs.Last().Message);

        app.Ui.ClipboardImage = (new byte[TaskAttachment.MaxFileBytes + 1], ".png");
        await page.Handler("OnPasteAttachmentClicked");
        Assert.Equal(app.Texts.Format("FileTooBig", 5), app.Ui.Dialogs.Last().Message);

        app.Ui.ClipboardImage = ([9, 9], ".jpg");
        await page.Handler("OnPasteAttachmentClicked");
        var pasted = (await app.Repository.GetAttachmentsAsync(task.Id)).Single();
        Assert.StartsWith(app.Texts["PastedImageName"], pasted.Name);
        Assert.EndsWith(".jpg", pasted.Name);

        // Un fallo al leer el portapapeles se cuenta en vez de cerrar la aplicacion.
        app.Ui.ClipboardFails = new InvalidOperationException("portapapeles ocupado");
        await page.Handler("OnPasteAttachmentClicked");
        Assert.Equal("portapapeles ocupado", app.Ui.Dialogs.Last().Message);
    });

    [Fact]
    public void Una_imagen_pegada_en_las_notas_se_guarda_como_adjunto() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Captura");
        var task = tasks[0];
        var page = await OpenAsync(app, task);

        // Es lo que llama el cuadro de texto de Android al pegar una imagen o recibirla del teclado.
        await page.Call("AddPastedImageAsync", new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, ".png");
        var pasted = (await app.Repository.GetAttachmentsAsync(task.Id)).Single();
        Assert.StartsWith(app.Texts["PastedImageName"], pasted.Name);
        Assert.EndsWith(".png", pasted.Name);
        Assert.Single(page.Named<VerticalStackLayout>("AttachmentsBox"));

        // Y lo dice: el adjunto queda fuera de la vista, mas abajo, y sin aviso parecia que no
        // hubiera pasado nada (el fallo del 2026-10-07).
        var banner = page.Named<Border>("PastedBanner");
        Assert.True(banner.IsVisible);
        Assert.Equal(app.Texts["PastedImageAdded"], page.Named<Label>("PastedBannerLabel").Text);

        // Se va solo al cumplirse su tiempo.
        var timer = TestDispatcher.Instance.Timers.Last();
        Assert.True(timer.IsRunning);
        timer.Fire();
        Assert.False(banner.IsVisible);
        Assert.False(timer.IsRunning);

        // Demasiado grande: se avisa y no se guarda (ni se dice que se ha añadido).
        await page.Call("AddPastedImageAsync", new byte[TaskAttachment.MaxFileBytes + 1], ".jpg");
        Assert.Equal(app.Texts.Format("FileTooBig", 5), app.Ui.Dialogs.Last().Message);
        Assert.Single(await app.Repository.GetAttachmentsAsync(task.Id));
        Assert.False(banner.IsVisible);
    });

    [Fact]
    public void El_boton_de_pegar_tambien_avisa_al_guardar_la_imagen() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Captura");
        var page = await OpenAsync(app, tasks[0]);

        var banner = page.Named<Border>("PastedBanner");
        await page.Handler("OnPasteAttachmentClicked");
        Assert.False(banner.IsVisible);                    // no habia imagen: solo el dialogo

        app.Ui.ClipboardImage = ([0xFF, 0xD8, 0xFF], ".jpg");
        await page.Handler("OnPasteAttachmentClicked");
        Assert.True(banner.IsVisible);

        // Otra antes de que se vaya: sigue a la vista, con la cuenta vuelta a empezar.
        await page.Handler("OnPasteAttachmentClicked");
        Assert.True(banner.IsVisible);
        Assert.True(TestDispatcher.Instance.Timers.Last().IsRunning);
    });

    [Fact]
    public void Una_imagen_dentro_del_html_copiado_se_reconoce()
    {
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 1, 2 };
        var b64 = Convert.ToBase64String(png);

        // En un <img> de HTML, como lo copian el correo, las notas o un chat.
        var html = $"<p>Mira:</p><img alt=\"x\" src=\"data:image/png;base64,{b64}\">";
        var image = Mobile.Services.PastedImage.FromDataUri(html);
        Assert.NotNull(image);
        Assert.Equal(png, image.Value.Bytes);
        Assert.Equal(".png", image.Value.Extension);

        // El texto entero, con el base64 partido en lineas; el tipo manda aunque los bytes no lo digan.
        var jpeg = Mobile.Services.PastedImage.FromDataUri("DATA:IMAGE/JPEG;BASE64," + b64[..4] + "\r\n" + b64[4..]);
        Assert.Equal(".jpg", jpeg?.Extension);
        Assert.Equal(png, jpeg?.Bytes);

        // Con entidades de salto de linea dentro del atributo.
        var entities = Mobile.Services.PastedImage.FromDataUri($"<img src=\"data:image/gif;base64,{b64[..4]}&#10;{b64[4..]}\">");
        Assert.Equal(".gif", entities?.Extension);

        // Si la primera esta rota se mira la siguiente.
        var second = Mobile.Services.PastedImage.FromDataUri($"<img src=\"data:image/png;base64,@@@\"><img src=\"data:image/png;base64,{b64}\">");
        Assert.Equal(png, second?.Bytes);
        var broken = Mobile.Services.PastedImage.FromDataUri("data:image/png;base64,abc");
        Assert.Null(broken);

        // Sin imagen dentro: nada (ni texto, ni una direccion https, ni un data: que no es imagen).
        Assert.Null(Mobile.Services.PastedImage.FromDataUri(null));
        Assert.Null(Mobile.Services.PastedImage.FromDataUri(""));
        Assert.Null(Mobile.Services.PastedImage.FromDataUri("hola"));
        Assert.Null(Mobile.Services.PastedImage.FromDataUri("<img src=\"https://example.com/a.png\">"));
        Assert.Null(Mobile.Services.PastedImage.FromDataUri("data:text/plain;base64," + b64));
    }

    [Theory]
    [InlineData("image/png", new byte[] { 1 }, ".png")]
    [InlineData("image/jpeg", new byte[] { 1 }, ".jpg")]
    [InlineData("IMAGE/GIF", new byte[] { 1 }, ".gif")]
    [InlineData("image/webp", new byte[] { 1 }, ".webp")]
    [InlineData("image/heif", new byte[] { 1 }, ".heic")]
    [InlineData("image/x-raro", new byte[] { 1 }, ".png")]
    [InlineData(null, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 }, ".png")]
    [InlineData("application/octet-stream", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, ".jpg")]
    [InlineData(null, new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' }, ".gif")]
    [InlineData(null, new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, ".webp")]
    [InlineData(null, new byte[] { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c' }, ".heic")]
    [InlineData(null, new byte[] { (byte)'B', (byte)'M', 0, 0 }, ".bmp")]
    [InlineData("text/plain", new byte[] { (byte)'h', (byte)'o', (byte)'l', (byte)'a' }, null)]
    [InlineData(null, new byte[0], null)]
    public void Se_reconoce_la_imagen_pegada_por_su_tipo_o_por_sus_bytes(string? mime, byte[] bytes, string? expected) =>
        Assert.Equal(expected, Mobile.Services.PastedImage.Extension(mime, bytes));

    [Fact]
    public void Borrar_una_tarea_suelta_pregunta_antes() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Comprar pan");
        var page = await OpenAsync(app, tasks[0]);

        await page.Handler("OnDeleteClicked");
        Assert.NotNull(await app.Repository.GetTaskAsync(tasks[0].Id));
        Assert.Empty(app.Ui.Routes);

        app.Ui.Answer(true);
        await page.Handler("OnDeleteClicked");
        Assert.Null(await app.Repository.GetTaskAsync(tasks[0].Id));
        Assert.Equal([".."], app.Ui.Routes);
    });

    [Fact]
    public void Borrar_una_vuelta_de_una_serie_pregunta_si_esta_o_todas() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Regar");
        var task = tasks[0];
        task.PlannedFor = DateTime.Today;
        task.DueAt = DateTime.Today.AddDays(3);
        task.RecurrenceRule = new Recurrence(RecurrenceKind.Daily, 1).Serialize();
        await app.Repository.UpdateTaskAsync(task);
        await app.Tasks.GenerateSeriesAsync(task);
        var series = task.SeriesId!.Value;
        var all = await app.Repository.GetSeriesAsync(series);
        Assert.Equal(4, all.Count);

        // Cancelar: nada.
        var page = await OpenAsync(app, all[1]);
        await page.Handler("OnDeleteClicked");
        Assert.Equal(4, (await app.Repository.GetSeriesAsync(series)).Count);
        Assert.Equal(app.Texts.Format("DeleteWholeSeries", 4), app.Ui.Dialogs.Last().Options[1]);

        // Solo esta.
        app.Ui.Answer(app.Texts["DeleteThisOnly"]);
        await page.Handler("OnDeleteClicked");
        Assert.Equal(3, (await app.Repository.GetSeriesAsync(series)).Count);

        // Toda la serie.
        page = await OpenAsync(app, all[2]);
        app.Ui.Answer(app.Texts.Format("DeleteWholeSeries", 3));
        await page.Handler("OnDeleteClicked");
        Assert.Empty(await app.Repository.GetSeriesAsync(series));
        Assert.Equal(2, app.Ui.Routes.Count);
    });

    [Fact]
    public void Atras_con_cambios_lo_pide_el_dispatcher_y_pregunta() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Comprar pan");
        var page = await OpenAsync(app, tasks[0]);
        page.Named<Entry>("TagsEntry").Text = "nueva";

        app.Ui.Answer(app.Texts["Discard"]);
        Assert.True(page.HandleBack());
        await UiThread.IdleAsync();
        await Task.Delay(20);
        await UiThread.IdleAsync();

        Assert.Equal([".."], app.Ui.Routes);
    });
}
