using System.Collections.ObjectModel;
using TaskManager.Mobile.Models;
using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// Detalle de una tarea: se edita lo que hay que hacer, el **contexto** que precisa el desglose,
/// las etiquetas, el plazo y cada cuanto se repite, y se ven y marcan sus micro-pasos.
/// </summary>
/// <remarks>
/// Nace de una nota de autor del 2026-08-29 ("permitir editar las tareas y ver los pasos
/// propuestos"): desde Mi Dia solo se podia completar una tarea, no tocarla.
/// </remarks>
[QueryProperty(nameof(TaskId), "taskId")]
public partial class TaskDetailPage : ContentPage
{
    private ObservableCollection<StepRow> _steps = [];
    private readonly List<TaskList> _lists = [];
    private byte _days;
    private byte _monthDay;
    private byte _month;

    private static readonly RecurrenceKind[] Kinds =
        [RecurrenceKind.None, RecurrenceKind.Daily, RecurrenceKind.Weekly, RecurrenceKind.Monthly, RecurrenceKind.Yearly];

    private readonly TaskService _tasks;
    private readonly SettingsService _settings;
    private readonly INotificationService _notifications;

    private TaskItem? _task;
    private Guid _taskId;
    private bool _loading;

    public TaskDetailPage()
        : this(ServiceHelper.GetRequiredService<TaskService>(),
               ServiceHelper.GetRequiredService<SettingsService>(),
               ServiceHelper.GetRequiredService<INotificationService>())
    {
    }

    public TaskDetailPage(TaskService tasks, SettingsService settings, INotificationService notifications)
    {
        InitializeComponent();

        _tasks = tasks;
        _settings = settings;
        _notifications = notifications;

        RecurrencePicker.ItemsSource = new List<string>
        {
            Localization.Loc.Instance["RepeatNever"], Localization.Loc.Instance["RepeatDaily"], Localization.Loc.Instance["RepeatWeekly"],
            Localization.Loc.Instance["RepeatMonthly"], Localization.Loc.Instance["RepeatYearly"],
        };
    }

    /// <summary>Llega por la ruta: <c>TaskDetailPage?taskId=...</c>.</summary>
    public string TaskId
    {
        set => _taskId = Guid.TryParse(Uri.UnescapeDataString(value), out var id) ? id : Guid.Empty;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        Celebration.HapticsEnabled = _settings.HapticsEnabled;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_taskId == Guid.Empty)
        {
            return;
        }

        _task = await _tasks.Repository.GetTaskAsync(_taskId);
        if (_task is null)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        // Bandera de carga: rellenar los controles dispara sus eventos, y sin esto se reescribiria
        // la tarea con lo que aun se esta pintando.
        _loading = true;

        Title = _task.Title;
        DoneSwitch.IsToggled = _task.IsDone;
        PrioritySwitch.IsToggled = _task.IsPriority;
        TitleEntry.Text = _task.Title;
        NotesEditor.Text = _task.Notes;
        TagsEntry.Text = TaskTags.ToInput(_task.Tags);

        // Rango acotado: sin el, el selector de Android abre la lista de años entera y cuesta
        // llegar al mes y al dia (nota de autor del 2026-08-29).
        var floor = DateTime.Now.Date.AddYears(-1);
        var ceiling = DateTime.Now.Date.AddYears(5);

        DuePicker.MinimumDate = floor;
        DuePicker.MaximumDate = ceiling;
        PlannedPicker.MinimumDate = floor;
        PlannedPicker.MaximumDate = ceiling;

        DueSwitch.IsToggled = _task.DueAt is not null;
        DuePicker.IsVisible = _task.DueAt is not null;
        DuePicker.Date = _task.DueAt?.Date ?? DateTime.Now.Date;

        PlannedSwitch.IsToggled = _task.PlannedFor is not null;
        PlannedPicker.IsVisible = _task.PlannedFor is not null;
        PlannedPicker.Date = _task.PlannedFor?.Date ?? DateTime.Now.Date;

        var recurrence = _task.Recurrence;
        _days = recurrence.Days;
        _monthDay = recurrence.MonthDay;
        _month = recurrence.Month;

        BuildWeekdays();
        BuildMonthDays();
        BuildMonths();

        RecurrencePicker.SelectedIndex = Array.IndexOf(Kinds, recurrence.Kind) is var index && index >= 0 ? index : 0;
        IntervalStepper.Value = Math.Clamp(recurrence.Interval <= 0 ? 1 : recurrence.Interval, 1, 30);
        IntervalStepper.IsVisible = recurrence.Kind != RecurrenceKind.None;
        RecurrenceLabel.Text = recurrence.Describe();

        _loading = false;

        await LoadListsAsync();
        await LoadKnownTagsAsync();
        await LoadStepsAsync();
        await LoadAttachmentsAsync();
    }

    // ==================================================================================
    //  Pasos
    // ==================================================================================

    /// <summary>
    /// Carga las listas y marca la de esta tarea. El desplegable no tiene opcion vacia a proposito:
    /// <b>ninguna tarea puede quedarse sin lista</b>.
    /// </summary>
    private async Task LoadListsAsync()
    {
        if (_task is null)
        {
            return;
        }

        _lists.Clear();
        _lists.AddRange(await _tasks.Repository.GetPrivateListsAsync());

        ListPicker.ItemsSource = _lists.Select(l => l.Name).ToList();

        var index = _lists.FindIndex(l => l.Id == _task.ListId);
        ListPicker.SelectedIndex = index >= 0 ? index : 0;
    }

    /// <summary>
    /// Pinta las etiquetas que ya existen en otras tareas. Tocar una la pone o la quita de esta.
    /// </summary>
    /// <remarks>
    /// Es lo que evita que la misma idea acabe escrita de tres formas —«casa», «Casa», «casaa»— y
    /// que el filtro por etiquetas se llene de duplicados que no agrupan nada.
    /// </remarks>
    private async Task LoadKnownTagsAsync()
    {
        var tags = await _tasks.Repository.GetTagsAsync();

        KnownTagsScroll.IsVisible = tags.Count > 0;
        KnownTagsBox.Clear();

        foreach (var tag in tags)
        {
            KnownTagsBox.Add(BuildTagChip(tag));
        }
    }

    private View BuildTagChip(string tag)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var active = TaskTags.Split(TaskTags.FromInput(TagsEntry.Text))
                             .Contains(tag, StringComparer.CurrentCultureIgnoreCase);

        var button = new Button
        {
            Text = $"#{tag}",
            FontSize = 13,
            Padding = new Thickness(14, 6),
            MinimumHeightRequest = 0,
            CornerRadius = 16,
            BackgroundColor = active
                ? Color.FromArgb("#3525CD")
                : Color.FromArgb(dark ? "#2A2833" : "#EDEEEF"),
            TextColor = active ? Colors.White : Color.FromArgb(dark ? "#E6E1E9" : "#191C1D"),
        };

        button.Clicked += async (_, _) =>
        {
            var current = TaskTags.Split(TaskTags.FromInput(TagsEntry.Text)).ToList();

            if (current.RemoveAll(t => string.Equals(t, tag, StringComparison.CurrentCultureIgnoreCase)) == 0)
            {
                current.Add(tag);
            }

            TagsEntry.Text = TaskTags.ToInput(TaskTags.Join(current));
            await LoadKnownTagsAsync();
        };

        return button;
    }

    private async Task LoadStepsAsync()
    {
        if (_task is null)
        {
            return;
        }

        var steps = await _tasks.Repository.GetStepsAsync(_task.Id);

        // Coleccion observable, no una lista: al arrastrar, CollectionView mueve el elemento dentro
        // de la propia fuente, y con una List<> corriente el cambio no se ve.
        _steps = new ObservableCollection<StepRow>(steps.Select(s => new StepRow(s)));
        StepsView.ItemsSource = _steps;

        NoStepsLabel.IsVisible = steps.Count == 0;
    }

    private async void OnAddStepClicked(object? sender, EventArgs e)
    {
        if (_task is null)
        {
            return;
        }

        var title = NewStepEntry.Text?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return;
        }

        await _tasks.Repository.AddStepsAsync(_task.Id, [title]);
        NewStepEntry.Text = string.Empty;
        await LoadStepsAsync();
    }

    private async void OnDeleteStepClicked(object? sender, EventArgs e)
    {
        if (sender is not ImageButton { CommandParameter: Guid id })
        {
            return;
        }

        var step = await _tasks.Repository.GetStepAsync(id);
        if (step is null)
        {
            return;
        }

        await _tasks.Repository.DeleteStepAsync(step);
        await LoadStepsAsync();
    }

    /// <summary>
    /// Guarda el orden despues de arrastrar.
    /// </summary>
    /// <remarks>
    /// No se recarga la lista al terminar: CollectionView ya ha dejado las filas donde el usuario
    /// las ha soltado, y volver a pintarlas provoca un parpadeo justo cuando acaba de levantar el
    /// dedo. Lo unico que hace falta es persistir el orden que ya se ve.
    /// </remarks>
    private async void OnStepsReordered(object? sender, EventArgs e)
    {
        await _tasks.Repository.ReorderStepsAsync([.. _steps.Select(r => r.Id)]);
    }

    /// <summary>
    /// Las siete pastillas de los dias, en el orden de la semana europea (lunes primero) aunque la
    /// mascara se guarde con domingo en el bit 0, que es como numera <see cref="DayOfWeek"/>.
    /// </summary>
    /// <summary>
    /// Los dias del mes elegibles, con «el mismo dia» como primera opcion: el que ya tuviera la
    /// tarea, que es como se comportaba antes de poder elegir.
    /// </summary>
    private void BuildMonthDays()
    {
        var options = new List<string> { Localization.Loc.Instance["MonthDaySame"] };
        options.AddRange(Enumerable.Range(1, 31).Select(d => d.ToString()));

        MonthDayPicker.ItemsSource = options;
        MonthDayPicker.SelectedIndex = Math.Clamp((int)_monthDay, 0, 31);
    }

    /// <summary>Los doce meses, con «el mismo mes» delante: el que ya tuviera la tarea.</summary>
    private void BuildMonths()
    {
        var names = System.Globalization.CultureInfo
            .GetCultureInfo(Localization.Loc.Instance.Language)
            .DateTimeFormat.MonthNames;

        var options = new List<string> { Localization.Loc.Instance["MonthSame"] };
        options.AddRange(names.Take(12).Select(n => char.ToUpperInvariant(n[0]) + n[1..]));

        MonthPicker.ItemsSource = options;
        MonthPicker.SelectedIndex = Math.Clamp((int)_month, 0, 12);
    }

    private void OnMonthChanged(object? sender, EventArgs e)
    {
        _month = (byte)Math.Clamp(MonthPicker.SelectedIndex, 0, 12);
        OnRecurrenceChanged(sender, e);
    }

    private void OnMonthDayChanged(object? sender, EventArgs e)
    {
        _monthDay = (byte)Math.Clamp(MonthDayPicker.SelectedIndex, 0, 31);
        OnRecurrenceChanged(sender, e);
    }

    private void BuildWeekdays()
    {
        WeekdaysBox.Clear();

        DayOfWeek[] order =
        [
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
        ];

        var names = System.Globalization.CultureInfo
            .GetCultureInfo(Localization.Loc.Instance.Language)
            .DateTimeFormat.AbbreviatedDayNames;

        foreach (var day in order)
        {
            var bit = (byte)(1 << (int)day);
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var active = (_days & bit) != 0;

            var button = new Button
            {
                Text = names[(int)day].TrimEnd('.').ToUpperInvariant(),
                FontSize = 12,
                Padding = new Thickness(10, 6),
                MinimumWidthRequest = 44,
                MinimumHeightRequest = 0,
                CornerRadius = 16,
                BackgroundColor = active
                    ? Color.FromArgb("#3525CD")
                    : Color.FromArgb(dark ? "#2A2833" : "#EDEEEF"),
                TextColor = active ? Colors.White : Color.FromArgb(dark ? "#E6E1E9" : "#191C1D"),
            };

            button.Clicked += (_, _) =>
            {
                _days = (byte)((_days & bit) != 0 ? _days & ~bit : _days | bit);
                BuildWeekdays();
                OnRecurrenceChanged(null, EventArgs.Empty);
            };

            WeekdaysBox.Add(button);
        }
    }

    /// <summary>
    /// Marca o desmarca la tarea sin salir del detalle. Completar suma XP y celebra; deshacer lo
    /// devuelve sin castigar, igual que en la lista.
    /// </summary>
    private async void OnDoneToggled(object? sender, ToggledEventArgs e)
    {
        if (_task is null || _task.IsDone == e.Value)
        {
            return;
        }

        if (e.Value)
        {
            var celebration = await _tasks.CompleteTaskAsync(_task);
            if (celebration is not null)
            {
                Celebration.Celebrate(celebration);
            }
        }
        else
        {
            await _tasks.UncompleteTaskAsync(_task);
        }
    }

    /// <summary>
    /// Toca el texto de un paso para cambiarlo. Antes solo se podia borrar y reescribir, que ademas
    /// le hacia perder su sitio en el orden.
    /// </summary>
    private async void OnEditStepClicked(object? sender, EventArgs e)
    {
        if (_task is null || sender is not ImageButton { CommandParameter: Guid id })
        {
            return;
        }

        var step = await _tasks.Repository.GetStepAsync(id);
        if (step is null)
        {
            return;
        }

        var written = await SocShared.ModernDialog.PromptAsync(
            this, Localization.Loc.Instance["EditStepTooltip"], null,
            Localization.Loc.Instance["Save"], Localization.Loc.Instance["Cancel"], step.Title);

        if (!string.IsNullOrWhiteSpace(written))
        {
            await _tasks.Repository.RenameStepAsync(step, written);
            await LoadStepsAsync();
        }
    }

    // -----------------------------------------------------------------------
    // Enlaces y ficheros
    // -----------------------------------------------------------------------

    private async Task LoadAttachmentsAsync()
    {
        if (_task is null)
        {
            return;
        }

        var items = await _tasks.Repository.GetAttachmentsAsync(_task.Id);

        AttachmentsBox.Clear();
        NoAttachmentsLabel.IsVisible = items.Count == 0;

        foreach (var item in items)
        {
            AttachmentsBox.Add(BuildAttachmentRow(item));
        }
    }

    /// <summary>Fila de adjunto: icono segun sea enlace o fichero, nombre, detalle y papelera.</summary>
    private View BuildAttachmentRow(TaskAttachment item)
    {
        var icon = new Image
        {
            Source = item.IsUrl ? "ic_link.png" : "ic_attach.png",
            WidthRequest = 20,
            HeightRequest = 20,
            VerticalOptions = LayoutOptions.Center,
        };

        var text = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 2 };
        text.Add(new Label { Text = item.Name, Style = (Style)Application.Current!.Resources["ItemTitle"] });
        text.Add(new Label
        {
            Text = item.IsUrl ? item.Url : item.SizeCaption,
            Style = (Style)Application.Current!.Resources["ItemSubtitle"],
        });

        var open = new TapGestureRecognizer();
        open.Tapped += async (_, _) => await OpenAttachmentAsync(item);
        text.GestureRecognizers.Add(open);

        var remove = new ImageButton
        {
            Source = "ic_delete_danger.png",
            Style = (Style)Application.Current!.Resources["RowIconButton"],
            HeightRequest = 34,
            WidthRequest = 34,
        };

        remove.Clicked += async (_, _) =>
        {
            await _tasks.Repository.DeleteAttachmentAsync(item);
            await LoadAttachmentsAsync();
        };

        var row = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            ],
            ColumnSpacing = 8,
            Padding = new Thickness(0, 4),
        };

        row.Add(icon, 0, 0);
        row.Add(text, 1, 0);
        row.Add(remove, 2, 0);

        return row;
    }

    private async void OnAddLinkClicked(object? sender, EventArgs e)
    {
        if (_task is null)
        {
            return;
        }

        var url = await SocShared.ModernDialog.PromptAsync(
            this, Localization.Loc.Instance["AddLinkTooltip"], null,
            Localization.Loc.Instance["Save"], Localization.Loc.Instance["Cancel"], null,
            Localization.Loc.Instance["LinkPlaceholder"]);

        if (!string.IsNullOrWhiteSpace(url))
        {
            await _tasks.Repository.AddLinkAsync(_task.Id, url);
            await LoadAttachmentsAsync();
        }
    }

    /// <summary>
    /// Mete un fichero <b>dentro</b> de la tarea.
    /// </summary>
    /// <remarks>
    /// Se guardan los bytes, no la ruta: una ruta de este movil no significa nada en Windows, y el
    /// adjunto tiene que viajar con la tarea. Por eso hay tope de tamaño, y si se pasa se dice.
    /// </remarks>
    private async void OnAddFileClicked(object? sender, EventArgs e)
    {
        if (_task is null)
        {
            return;
        }

        try
        {
            var picked = await FilePicker.Default.PickAsync();
            if (picked is null)
            {
                return;
            }

            using var stream = await picked.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);

            if (memory.Length > TaskAttachment.MaxFileBytes)
            {
                await SocShared.ModernDialog.AlertAsync(this,
                    Localization.Loc.Instance["AddFileTooltip"],
                    Localization.Loc.Instance.Format("FileTooBig", TaskAttachment.MaxFileBytes / (1024 * 1024)),
                    "OK");
                return;
            }

            await _tasks.Repository.AddFileAsync(_task.Id, picked.FileName, memory.ToArray());
            await LoadAttachmentsAsync();
        }
        catch (Exception ex)
        {
            await SocShared.ModernDialog.AlertAsync(this,
                Localization.Loc.Instance["AddFileTooltip"], ex.Message, "OK");
        }
    }

    /// <summary>
    /// Abre el adjunto: el enlace en el navegador, el fichero con la aplicacion que le toque.
    /// </summary>
    /// <remarks>
    /// El fichero vive en la base de datos, asi que para abrirlo hay que volcarlo antes a disco. Se
    /// deja en la carpeta de cache, que es de donde el sistema limpia solo.
    /// </remarks>
    private async Task OpenAttachmentAsync(TaskAttachment item)
    {
        try
        {
            if (item.IsUrl)
            {
                await Browser.Default.OpenAsync(item.Url, BrowserLaunchMode.SystemPreferred);
                return;
            }

            var folder = Path.Combine(FileSystem.CacheDirectory, "adjuntos", item.Id.ToString("N"));
            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, item.Name);
            await File.WriteAllBytesAsync(path, item.Data ?? []);

            await Launcher.Default.OpenAsync(new OpenFileRequest(item.Name, new ReadOnlyFile(path)));
        }
        catch (Exception ex)
        {
            await SocShared.ModernDialog.AlertAsync(this,
                Localization.Loc.Instance["Attachments"], ex.Message, "OK");
        }
    }

    private async void OnToggleStepClicked(object? sender, EventArgs e)
    {
        if (sender is not ImageButton { CommandParameter: Guid id })
        {
            return;
        }

        var step = await _tasks.Repository.GetStepAsync(id);
        if (step is null)
        {
            return;
        }

        var celebration = await _tasks.ToggleStepAsync(step);
        await LoadStepsAsync();

        if (celebration is not null)
        {
            Celebration.Celebrate(celebration);
        }
    }

    /// <summary>
    /// Propone pasos a partir del titulo y del contexto. Antes de proponer se guarda lo escrito:
    /// de nada sirve un contexto que todavia esta solo en pantalla.
    /// </summary>
    private async void OnBreakdownClicked(object? sender, EventArgs e)
    {
        if (_task is null)
        {
            return;
        }

        await SaveAsync(silent: true);

        WandButton.IsEnabled = false;
        StepsBusy.IsRunning = true;
        StepsBusy.IsVisible = true;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var proposal = await _tasks.ProposeBreakdownAsync(_task, cts.Token);

            if (!proposal.HasSomethingNew)
            {
                await SocShared.ModernDialog.AlertAsync(this, Localization.Loc.Instance["MagicSteps"],
                    proposal.AlreadyPresent > 0 ? Localization.Loc.Instance["MagicAllPresent"] : Localization.Loc.Instance["MagicNothing"],
                    "OK");
                return;
            }

            var detail = "• " + string.Join("\n• ", proposal.Steps);
            if (proposal.AlreadyPresent > 0)
            {
                detail += "\n\n" + Localization.Loc.Instance.Format("MagicDiscarded", proposal.AlreadyPresent);
            }

            var accepted = await SocShared.ModernDialog.AlertAsync(this,
                $"{Localization.Loc.Instance["MagicSteps"]} · {proposal.Source}", detail, Localization.Loc.Instance["MagicAdd"], Localization.Loc.Instance["MagicNotNow"]);

            if (!accepted)
            {
                return;
            }

            var (_, celebration) = await _tasks.ApplyBreakdownAsync(_task, proposal.Steps);
            await LoadStepsAsync();

            if (celebration is not null)
            {
                Celebration.Celebrate(celebration);
            }
        }
        finally
        {
            StepsBusy.IsRunning = false;
            StepsBusy.IsVisible = false;
            WandButton.IsEnabled = true;
        }
    }

    // ==================================================================================
    //  Plazo y repeticion
    // ==================================================================================

    private void OnDueToggled(object? sender, ToggledEventArgs e)
    {
        DuePicker.IsVisible = e.Value;
    }

    private void OnPlannedToggled(object? sender, ToggledEventArgs e)
    {
        PlannedPicker.IsVisible = e.Value;
    }

    private void OnRecurrenceChanged(object? sender, EventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var kind = Kinds[Math.Clamp(RecurrencePicker.SelectedIndex, 0, Kinds.Length - 1)];
        var recurrence = new Recurrence(kind, (int)IntervalStepper.Value, _days, _monthDay, _month);

        IntervalStepper.IsVisible = kind != RecurrenceKind.None;

        // Elegir dias solo tiene sentido en la diaria y la semanal.
        WeekdaysScroll.IsVisible = recurrence.UsesDays;
        MonthDayRow.IsVisible = recurrence.UsesMonthDay;
        MonthRow.IsVisible = recurrence.UsesMonth;
        RecurrenceLabel.Text = recurrence.Describe();
    }

    private void OnIntervalChanged(object? sender, ValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var kind = Kinds[Math.Clamp(RecurrencePicker.SelectedIndex, 0, Kinds.Length - 1)];
        RecurrenceLabel.Text = new Recurrence(kind, (int)e.NewValue).Describe();
    }

    // ==================================================================================
    //  Guardar y borrar
    // ==================================================================================

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (await SaveAsync(silent: false))
        {
            await Shell.Current.GoToAsync("..");
        }
    }

    private async Task<bool> SaveAsync(bool silent)
    {
        if (_task is null)
        {
            return false;
        }

        var title = TitleEntry.Text?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            if (!silent)
            {
                await SocShared.ModernDialog.AlertAsync(this,
                    Localization.Loc.Instance["NeedTitleTitle"], Localization.Loc.Instance["NeedTitleMessage"], "OK");
            }

            return false;
        }

        _task.Title = title;
        _task.Notes = NotesEditor.Text?.Trim() ?? string.Empty;
        _task.Tags = TaskTags.FromInput(TagsEntry.Text);
        // DatePicker.Date es nullable desde MAUI 10.
        _task.DueAt = DueSwitch.IsToggled ? DuePicker.Date?.Date : null;
        _task.PlannedFor = PlannedSwitch.IsToggled ? PlannedPicker.Date?.Date : null;

        var kind = Kinds[Math.Clamp(RecurrencePicker.SelectedIndex, 0, Kinds.Length - 1)];
        _task.RecurrenceRule = new Recurrence(kind, (int)IntervalStepper.Value, _days, _monthDay, _month).Serialize();
        _task.IsPriority = PrioritySwitch.IsToggled;

        if (ListPicker.SelectedIndex >= 0 && ListPicker.SelectedIndex < _lists.Count)
        {
            _task.ListId = _lists[ListPicker.SelectedIndex].Id;
        }

        await _tasks.Repository.UpdateTaskAsync(_task);

        // El aviso se reprograma con lo que acaba de guardarse: si se quito la fecha, se cancela.
        _notifications.ScheduleTaskReminder(_task);
        return true;
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_task is null)
        {
            return;
        }

        var confirmed = await SocShared.ModernDialog.AlertAsync(this,
            Localization.Loc.Instance["DeleteTask"], Localization.Loc.Instance.Format("DeleteTaskMessage", _task.Title),
            Localization.Loc.Instance["Delete"], Localization.Loc.Instance["Cancel"]);

        if (!confirmed)
        {
            return;
        }

        await _tasks.Repository.DeleteTaskAsync(_task);
        await Shell.Current.GoToAsync("..");
    }
}
