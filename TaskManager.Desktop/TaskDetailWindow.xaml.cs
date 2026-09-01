using System.Windows;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;
using TaskManager.Core.Models;
using TaskManager.Core.Services;

namespace TaskManager.Desktop;

/// <summary>
/// Detalle de una tarea en Windows, con los mismos campos que <c>TaskDetailPage</c> en Android.
/// </summary>
/// <remarks>
/// <para>Windows no tenia ninguna pantalla de detalle: se podia crear una tarea y marcarla hecha, y
/// nada mas. Todo lo que se configuraba en el movil —notas, etiquetas, fechas, repeticion, pasos—
/// llegaba aqui por la sincronizacion y no habia manera de tocarlo. Esta ventana cierra ese hueco,
/// campo por campo y en el mismo orden, para que cambiar de aparato no obligue a reaprender nada.</para>
///
/// <para>Se guarda al pulsar guardar, no sobre la marcha: en una ventana de escritorio se edita con
/// el teclado y guardar en cada pulsacion llenaria la cola de sincronizacion de versiones a medio
/// escribir. <b>Los pasos son la excepcion</b> y se guardan al momento: son acciones sueltas
/// (añadir, marcar, borrar), no texto que se este redactando.</para>
/// </remarks>
public partial class TaskDetailWindow : Window
{
    private static readonly RecurrenceKind[] Kinds =
        [RecurrenceKind.None, RecurrenceKind.Daily, RecurrenceKind.Weekly, RecurrenceKind.Monthly, RecurrenceKind.Yearly];

    private readonly TaskService _tasks;
    private readonly TaskItem _task;

    private readonly ObservableCollection<StepRow> _steps = [];
    private readonly List<TaskList> _lists = [];

    private int _interval = 1;
    private byte _days;
    private byte _monthDay;
    private byte _month;
    private Point _dragStart;

    public TaskDetailWindow(TaskService tasks, TaskItem task)
    {
        InitializeComponent();

        _tasks = tasks;
        _task = task;

        RecurrenceBox.ItemsSource = new List<string>
        {
            T("RepeatNever"), T("RepeatDaily"), T("RepeatWeekly"), T("RepeatMonthly"), T("RepeatYearly"),
        };

        StepsBox.ItemsSource = _steps;

        Fill();
        _ = LoadListsAsync();
        _ = ReloadStepsAsync();
    }

    /// <summary>Si algo cambio, para que quien abrio la ventana sepa si tiene que releer.</summary>
    public bool Changed { get; private set; }

    /// <summary>La tarea se borro: quien la enseñaba tiene que quitarla de su lista.</summary>
    public bool Deleted { get; private set; }

    private static string T(string key) => Localization.Loc.Get(key);

    private void Fill()
    {
        DoneCheck.IsChecked = _task.IsDone;
        TitleBox.Text = _task.Title;
        NotesBox.Text = _task.Notes;
        ShowTags(TaskTags.Split(_task.Tags));

        // Fecha y hora por separado: el DatePicker guarda el dia y el TimePicker la hora, y al
        // guardar se juntan. Una tarea que vence "el martes a las 9" no cabe en solo una fecha.
        DueCheck.IsChecked = _task.DueAt is not null;
        DuePicker.SelectedDate = _task.DueAt?.Date ?? DateTime.Today;
        DueTime.SelectedTime = _task.DueAt ?? DateTime.Today.AddHours(9);
        DueRow.Visibility = _task.DueAt is null ? Visibility.Collapsed : Visibility.Visible;

        PlannedCheck.IsChecked = _task.PlannedFor is not null;
        PlannedPicker.SelectedDate = _task.PlannedFor?.Date ?? DateTime.Today;
        PlannedTime.SelectedTime = _task.PlannedFor ?? DateTime.Today.AddHours(9);
        PlannedRow.Visibility = _task.PlannedFor is null ? Visibility.Collapsed : Visibility.Visible;

        var recurrence = _task.Recurrence;
        var index = Array.IndexOf(Kinds, recurrence.Kind);
        RecurrenceBox.SelectedIndex = index >= 0 ? index : 0;

        _interval = Math.Clamp(recurrence.Interval <= 0 ? 1 : recurrence.Interval, 1, 30);
        _days = recurrence.Days;
        _monthDay = recurrence.MonthDay;
        _month = recurrence.Month;

        BuildWeekdays();
        BuildMonthDays();
        BuildMonths();
        ShowRecurrence();
    }

    /// <summary>
    /// Carga las listas y marca la de esta tarea.
    /// </summary>
    /// <remarks>
    /// El desplegable no tiene opcion vacia a proposito: <b>ninguna tarea puede quedarse sin
    /// lista</b>, porque la lista es de donde cuelga y lo que decide quien la ve.
    /// </remarks>
    private async Task LoadListsAsync()
    {
        _lists.Clear();
        _lists.AddRange(await _tasks.Repository.GetPrivateListsAsync());

        ListBox.ItemsSource = _lists.Select(l => l.Name).ToList();

        var index = _lists.FindIndex(l => l.Id == _task.ListId);
        ListBox.SelectedIndex = index >= 0 ? index : 0;
    }

    /// <summary>El dia del calendario mas la hora del reloj. Sin hora, las 9 de la mañana.</summary>
    private static DateTime? Combine(DateTime? day, DateTime? time)
    {
        if (day is not { } d)
        {
            return null;
        }

        var t = time ?? d.Date.AddHours(9);
        return d.Date.Add(t.TimeOfDay);
    }

    /// <summary>Las etiquetas que hay ahora mismo en el contenedor.</summary>
    private List<string> CurrentTags() =>
        [.. TagsContainer.Items.OfType<HandyControl.Controls.Tag>()
                               .Select(t => t.Content?.ToString() ?? string.Empty)
                               .Where(t => t.Length > 0)];

    /// <summary>
    /// Pinta las etiquetas de la tarea como pastillas con aspa.
    /// </summary>
    /// <remarks>
    /// Es el <c>TagContainer</c> de HandyControl (constitucion Anexo B.2). Antes era un campo de
    /// texto con comas y una fila de pastillas hecha a mano: dos sitios donde mirar y un espacio de
    /// mas creaba una etiqueta distinta.
    /// </remarks>
    private void ShowTags(IEnumerable<string> tags)
    {
        TagsContainer.Items.Clear();

        foreach (var tag in tags)
        {
            var chip = new HandyControl.Controls.Tag
            {
                Content = tag,
                ShowCloseButton = true,
                Margin = new Thickness(0, 0, 6, 6),
            };

            chip.Closed += (sender, _) => TagsContainer.Items.Remove(sender);
            TagsContainer.Items.Add(chip);
        }
    }

    /// <summary>Enter en la caja da de alta la etiqueta escrita y limpia la caja.</summary>
    private void OnNewTagKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        var written = TaskTags.Split(TaskTags.FromInput(TagsBox.Text));
        if (written.Count == 0)
        {
            return;
        }

        var all = CurrentTags();
        all.AddRange(written);

        ShowTags(TaskTags.Split(TaskTags.Join(all)));
        TagsBox.Text = string.Empty;
    }

    /// <summary>
    /// Marca o desmarca la tarea, al momento. Completar suma XP y celebra; deshacer lo devuelve sin
    /// castigar, que es la misma regla que en la lista.
    /// </summary>
    private async void OnDoneToggled(object sender, RoutedEventArgs e)
    {
        if (DoneCheck.IsChecked == true)
        {
            await _tasks.CompleteTaskAsync(_task);
        }
        else
        {
            await _tasks.UncompleteTaskAsync(_task);
        }

        Changed = true;
    }

    // -----------------------------------------------------------------------
    // Fechas y repeticion
    // -----------------------------------------------------------------------

    private void OnDueToggled(object sender, RoutedEventArgs e) =>
        DueRow.Visibility = DueCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void OnPlannedToggled(object sender, RoutedEventArgs e) =>
        PlannedRow.Visibility = PlannedCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void OnRecurrenceChanged(object sender, SelectionChangedEventArgs e) => ShowRecurrence();

    private void OnIntervalUp(object sender, RoutedEventArgs e)
    {
        _interval = Math.Min(30, _interval + 1);
        ShowRecurrence();
    }

    private void OnIntervalDown(object sender, RoutedEventArgs e)
    {
        _interval = Math.Max(1, _interval - 1);
        ShowRecurrence();
    }

    /// <summary>
    /// Las siete pastillas de los dias, en el orden de la semana europea (lunes primero) aunque la
    /// mascara se guarde con domingo en el bit 0, que es como numera <see cref="DayOfWeek"/>.
    /// </summary>
    private void BuildWeekdays()
    {
        WeekdaysBox.Children.Clear();

        DayOfWeek[] order =
        [
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
        ];

        var names = System.Globalization.CultureInfo
            .GetCultureInfo(Localization.Loc.Language)
            .DateTimeFormat.AbbreviatedDayNames;

        foreach (var day in order)
        {
            var chip = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = names[(int)day].TrimEnd('.').ToUpperInvariant(),
                Style = (Style)FindResource("Chip"),
                IsChecked = (_days & (1 << (int)day)) != 0,
                Tag = day,
                MinWidth = 40,
            };

            chip.Click += (sender, _) =>
            {
                var bit = (byte)(1 << (int)(DayOfWeek)((System.Windows.Controls.Primitives.ToggleButton)sender).Tag);

                if (((System.Windows.Controls.Primitives.ToggleButton)sender).IsChecked == true)
                {
                    _days |= bit;
                }
                else
                {
                    _days &= (byte)~bit;
                }

                ShowRecurrence();
            };

            WeekdaysBox.Children.Add(chip);
        }
    }

    /// <summary>
    /// Los dias del mes elegibles, con «el mismo dia» como primera opcion.
    /// </summary>
    /// <remarks>
    /// «El mismo dia» es el valor de partida y significa: el que ya tuviera la tarea. Es lo que
    /// hacia antes de que se pudiera elegir, y sigue siendo lo razonable si nadie dice otra cosa.
    /// </remarks>
    private void BuildMonthDays()
    {
        var options = new List<string> { T("MonthDaySame") };
        options.AddRange(Enumerable.Range(1, 31).Select(d => d.ToString()));

        MonthDayBox.ItemsSource = options;
        MonthDayBox.SelectedIndex = Math.Clamp((int)_monthDay, 0, 31);
    }

    /// <summary>Los doce meses, con «el mismo mes» delante: el que ya tuviera la tarea.</summary>
    private void BuildMonths()
    {
        var names = System.Globalization.CultureInfo
            .GetCultureInfo(Localization.Loc.Language)
            .DateTimeFormat.MonthNames;

        var options = new List<string> { T("MonthSame") };
        options.AddRange(names.Take(12).Select(n => char.ToUpperInvariant(n[0]) + n[1..]));

        MonthBox.ItemsSource = options;
        MonthBox.SelectedIndex = Math.Clamp((int)_month, 0, 12);
    }

    private void OnMonthChanged(object sender, SelectionChangedEventArgs e)
    {
        _month = (byte)Math.Clamp(MonthBox.SelectedIndex, 0, 12);
        ShowRecurrence();
    }

    private void OnMonthDayChanged(object sender, SelectionChangedEventArgs e)
    {
        _monthDay = (byte)Math.Clamp(MonthDayBox.SelectedIndex, 0, 31);
        ShowRecurrence();
    }

    private void ShowRecurrence()
    {
        var kind = Kinds[Math.Clamp(RecurrenceBox.SelectedIndex, 0, Kinds.Length - 1)];
        var recurrence = new Recurrence(kind, _interval, _days, _monthDay, _month);

        IntervalPanel.Visibility = kind == RecurrenceKind.None ? Visibility.Collapsed : Visibility.Visible;

        // Elegir dias solo tiene sentido en la diaria y la semanal: «cada 3 meses pero solo en
        // martes» no describe nada que nadie quiera.
        WeekdaysBox.Visibility = recurrence.UsesDays ? Visibility.Visible : Visibility.Collapsed;
        MonthDayRow.Visibility = recurrence.UsesMonthDay ? Visibility.Visible : Visibility.Collapsed;

        var monthVisible = recurrence.UsesMonth ? Visibility.Visible : Visibility.Collapsed;
        MonthLabelText.Visibility = monthVisible;
        MonthBox.Visibility = monthVisible;

        IntervalLabel.Text = _interval.ToString();
        RecurrenceLabel.Text = recurrence.Describe();
    }

    // -----------------------------------------------------------------------
    // Pasos
    // -----------------------------------------------------------------------

    private async Task ReloadStepsAsync()
    {
        var steps = await _tasks.Repository.GetStepsAsync(_task.Id);

        _steps.Clear();
        foreach (var step in steps)
        {
            _steps.Add(new StepRow(step));
        }

        NoStepsLabel.Visibility = steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // -----------------------------------------------------------------------
    // Arrastrar para reordenar
    // -----------------------------------------------------------------------

    /// <summary>
    /// Solo se guarda donde empezo el gesto. El arrastre no arranca aqui: si lo hiciera, marcar un
    /// paso o pulsar su papelera se convertiria en un arrastre accidental.
    /// </summary>
    private void OnStepMouseDown(object sender, MouseButtonEventArgs e) =>
        _dragStart = e.GetPosition(null);

    /// <summary>
    /// Arranca el arrastre cuando el raton se ha movido lo bastante con el boton pulsado. El umbral
    /// del sistema (<see cref="SystemParameters.MinimumHorizontalDragDistance"/>) es justo el que
    /// separa un clic tembloroso de una intencion de arrastrar.
    /// </summary>
    private void OnStepMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var moved = e.GetPosition(null) - _dragStart;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (FindRow(e.OriginalSource as DependencyObject) is { Content: StepRow row })
        {
            DragDrop.DoDragDrop(StepsBox, row, DragDropEffects.Move);
        }
    }

    private async void OnStepDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(StepRow)) is not StepRow dragged)
        {
            return;
        }

        var target = FindRow(e.OriginalSource as DependencyObject)?.Content as StepRow;
        var from = _steps.IndexOf(dragged);
        var to = target is null ? _steps.Count - 1 : _steps.IndexOf(target);

        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        _steps.Move(from, to);

        // No se recarga: la lista ya esta como el usuario la ha dejado, y repintarla justo al
        // soltar da un parpadeo. Basta con guardar el orden que ya se ve.
        await _tasks.Repository.ReorderStepsAsync([.. _steps.Select(r => r.Id)]);
        Changed = true;
    }

    /// <summary>La fila sobre la que ha caido el raton, subiendo desde lo que se pulso.</summary>
    private static ListBoxItem? FindRow(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        return source as ListBoxItem;
    }

    private async void OnAddStepClick(object sender, RoutedEventArgs e) => await AddStepAsync();

    /// <summary>Enter añade el paso y deja el foco donde estaba, para poder encadenar varios.</summary>
    private async void OnNewStepKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await AddStepAsync();
        }
    }

    private async Task AddStepAsync()
    {
        var title = NewStepBox.Text?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return;
        }

        await _tasks.Repository.AddStepsAsync(_task.Id, [title]);
        NewStepBox.Text = string.Empty;
        NewStepBox.Focus();

        Changed = true;
        await ReloadStepsAsync();
    }

    private async void OnStepToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: Guid id })
        {
            return;
        }

        var steps = await _tasks.Repository.GetStepsAsync(_task.Id);
        var step = steps.FirstOrDefault(s => s.Id == id);
        if (step is null)
        {
            return;
        }

        await _tasks.ToggleStepAsync(step);
        Changed = true;
        await ReloadStepsAsync();
    }

    /// <summary>
    /// Doble clic en un paso: cambiarle el texto. Antes solo se podia borrarlo y volver a
    /// escribirlo, que ademas perdia su sitio en el orden.
    /// </summary>
    private async void OnStepDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;

        while (source is not null and not System.Windows.Controls.ListBoxItem)
        {
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        if (source is not System.Windows.Controls.ListBoxItem { Content: StepRow row })
        {
            return;
        }

        var written = Prompt.Ask(this, T("EditStepTooltip"), row.Title);
        if (string.IsNullOrWhiteSpace(written))
        {
            return;
        }

        var step = (await _tasks.Repository.GetStepsAsync(_task.Id)).FirstOrDefault(s => s.Id == row.Id);
        if (step is null)
        {
            return;
        }

        await _tasks.Repository.RenameStepAsync(step, written);
        Changed = true;
        await ReloadStepsAsync();
    }

    private async void OnDeleteStepClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id })
        {
            return;
        }

        var steps = await _tasks.Repository.GetStepsAsync(_task.Id);
        var step = steps.FirstOrDefault(s => s.Id == id);
        if (step is null)
        {
            return;
        }

        await _tasks.Repository.DeleteStepAsync(step);
        Changed = true;
        await ReloadStepsAsync();
    }

    /// <summary>
    /// Desglose con el modelo local. Cae a plantillas si no hay ninguno, asi que nunca se queda sin
    /// proponer nada; por eso el boton no se esconde aunque no haya IA.
    /// </summary>
    private async void OnBreakdownClick(object sender, RoutedEventArgs e)
    {
        BreakdownButton.IsEnabled = false;
        StatusLabel.Text = T("BreakdownWorking");
        HandyControl.Controls.Growl.InfoGlobal(T("BreakdownWorking"));

        try
        {
            // Se guarda el titulo y las notas primero: el desglose parte de ellos, y si el usuario
            // acaba de escribirlos, proponer sobre la version anterior seria desconcertante.
            ApplyFields();
            await _tasks.Repository.UpdateTaskAsync(_task);

            var proposal = await _tasks.ProposeBreakdownAsync(_task);
            if (proposal.Steps.Count == 0)
            {
                StatusLabel.Text = T("BreakdownNothing");
                HandyControl.Controls.Growl.WarningGlobal(T("BreakdownNothing"));
                return;
            }

            await _tasks.ApplyBreakdownAsync(_task, [.. proposal.Steps]);
            Changed = true;
            await ReloadStepsAsync();

            StatusLabel.Text = string.Empty;
        }
        catch (Exception ex)
        {
            // Growl y no solo la etiqueta: un fallo del desglose en una linea de 11 pixeles al pie
            // de la ventana no lo lee nadie.
            StatusLabel.Text = ex.Message;
            HandyControl.Controls.Growl.ErrorGlobal(ex.Message);
        }
        finally
        {
            BreakdownButton.IsEnabled = true;
        }
    }

    // -----------------------------------------------------------------------
    // Guardar y borrar
    // -----------------------------------------------------------------------

    private void ApplyFields()
    {
        _task.Title = TitleBox.Text?.Trim() ?? string.Empty;
        _task.Notes = NotesBox.Text?.Trim() ?? string.Empty;
        _task.Tags = TaskTags.Join(CurrentTags());

        _task.DueAt = DueCheck.IsChecked == true ? Combine(DuePicker.SelectedDate, DueTime.SelectedTime) : null;
        _task.PlannedFor = PlannedCheck.IsChecked == true
            ? Combine(PlannedPicker.SelectedDate, PlannedTime.SelectedTime)
            : null;

        var kind = Kinds[Math.Clamp(RecurrenceBox.SelectedIndex, 0, Kinds.Length - 1)];
        _task.RecurrenceRule = new Recurrence(kind, _interval, _days, _monthDay, _month).Serialize();

        if (ListBox.SelectedIndex >= 0 && ListBox.SelectedIndex < _lists.Count)
        {
            _task.ListId = _lists[ListBox.SelectedIndex].Id;
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            StatusLabel.Text = T("TitleRequired");
            HandyControl.Controls.Growl.WarningGlobal(T("TitleRequired"));
            return;
        }

        ApplyFields();
        await _tasks.Repository.UpdateTaskAsync(_task);

        Changed = true;
        DialogResult = true;
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var confirmed = Controls.ModernDialog.Confirm(
            this, T("DeleteTask"), Localization.Loc.Format("DeleteTaskConfirm", _task.Title), danger: true);

        if (!confirmed)
        {
            return;
        }

        await _tasks.Repository.DeleteTaskAsync(_task);

        Deleted = true;
        Changed = true;
        DialogResult = true;
    }

    /// <summary>Fila de paso lista para pintar, sin logica en el XAML.</summary>
    private sealed record StepRow(TaskStep Step)
    {
        public Guid Id => Step.Id;

        public string Title => Step.Title;

        public bool IsDone => Step.IsDone;

        public TextDecorationCollection? Decoration =>
            Step.IsDone ? TextDecorations.Strikethrough : null;

        public double Opacity => Step.IsDone ? 0.55 : 1.0;
    }
}
