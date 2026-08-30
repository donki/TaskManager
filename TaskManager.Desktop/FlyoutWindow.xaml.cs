using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TaskManager.Core.Gamification;
using TaskManager.Core.Models;
using TaskManager.Core.Services;

namespace TaskManager.Desktop;

/// <summary>
/// Panel rapido de la bandeja (especificacion 6.B): ver "Mi Dia", anadir una tarea en menos de dos
/// segundos y completarla con su celebracion, sin abrir ninguna ventana grande.
/// </summary>
public partial class FlyoutWindow : Window
{
    private readonly TaskService _tasks;
    private readonly SettingsService _settings;
    private readonly ObservableCollection<TaskRow> _rows = [];
    private readonly Dictionary<Guid, string> _listNames = [];

    private bool _closingForReal;

    public FlyoutWindow(TaskService tasks, SettingsService settings)
    {
        InitializeComponent();

        _tasks = tasks;
        _settings = settings;
        TaskList.ItemsSource = _rows;

        // La fecha va en el idioma elegido: en ingles «de» sobra y el dia de la semana cambia.
        DateLabel.Text = DateTime.Now.ToString(
            Localization.Loc.Get("DatePattern"),
            System.Globalization.CultureInfo.GetCultureInfo(Localization.Loc.Language));
        QuickAdd.Text = string.Empty;
    }

    /// <summary>Numero de tareas pendientes de hoy; lo consume el icono de la bandeja.</summary>
    public event EventHandler<int>? PendingChanged;

    public event EventHandler? SettingsRequested;

    public event EventHandler? CalendarRequested;

    public event EventHandler? MainRequested;

    // -----------------------------------------------------------------------
    // Mostrar y ocultar
    // -----------------------------------------------------------------------

    /// <summary>
    /// Se coloca sobre la bandeja, en la esquina del area de trabajo, para que aparezca donde el
    /// usuario acaba de hacer clic.
    /// </summary>
    public async void ShowFlyout()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width;
        Top = work.Bottom - Height;

        Visibility = Visibility.Visible;
        Show();
        Activate();

        await ReloadAsync();

        QuickAdd.Focus();
        Keyboard.Focus(QuickAdd);
    }

    public void HideFlyout()
    {
        QuickAdd.Text = string.Empty;
        Visibility = Visibility.Hidden;
        Hide();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        // Un panel de bandeja se cierra al perder el foco: si no, estorba encima de todo.
        if (IsVisible)
        {
            HideFlyout();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideFlyout();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>La X del sistema esconde, no cierra: la aplicacion vive en la bandeja.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closingForReal)
        {
            e.Cancel = true;
            HideFlyout();
            return;
        }

        base.OnClosing(e);
    }

    public void CloseForReal()
    {
        _closingForReal = true;
        Close();
    }

    // -----------------------------------------------------------------------
    // Datos
    // -----------------------------------------------------------------------

    public async Task ReloadAsync()
    {
        await ReloadListsAsync();

        var tasks = await _tasks.Repository.GetMyDayAsync();
        _rows.Clear();
        foreach (var task in tasks)
        {
            _rows.Add(new TaskRow(task, _listNames.GetValueOrDefault(task.ListId, string.Empty)));
        }

        var pending = tasks.Count(t => !t.IsDone);
        StatusLabel.Text = pending switch
        {
            0 when tasks.Count == 0 => Localization.Loc.Get("NothingInMyDay"),
            0 => Localization.Loc.Get("AllDone"),
            1 => Localization.Loc.Get("PendingOne"),
            _ => Localization.Loc.Format("PendingMany", pending),
        };
        HotkeyLabel.Text = _settings.Get(SettingsService.KeyHotkey, "Ctrl+Alt+T");

        var board = await _tasks.GetBoardAsync();
        LevelLabel.Text = Localization.Loc.Format("LevelShort", board.Level);
        LevelProgress.Value = board.ProgressInLevel;

        PendingChanged?.Invoke(this, pending);
    }

    private async Task ReloadListsAsync()
    {
        var selected = ListPicker.SelectedValue as Guid?;

        var options = new List<ListOption>();
        _listNames.Clear();

        foreach (var list in await _tasks.Repository.GetPrivateListsAsync())
        {
            options.Add(new ListOption(list.Id, list.Name));
            _listNames[list.Id] = list.Name;
        }

        foreach (var group in await _tasks.Repository.GetGroupsAsync())
        {
            foreach (var list in await _tasks.Repository.GetGroupListsAsync(group.Id))
            {
                options.Add(new ListOption(list.Id, $"{group.Name} · {list.Name}"));
                _listNames[list.Id] = $"{group.Name} · {list.Name}";
            }
        }

        ListPicker.ItemsSource = options;
        ListPicker.SelectedValue = selected is not null && options.Any(o => o.Id == selected)
            ? selected
            : options.FirstOrDefault()?.Id;
    }

    // -----------------------------------------------------------------------
    // Acciones
    // -----------------------------------------------------------------------

    private async void OnQuickAddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await AddTaskAsync();
        }
    }

    private async void OnAddClick(object sender, RoutedEventArgs e) => await AddTaskAsync();

    private async Task<TaskItem?> AddTaskAsync()
    {
        var title = QuickAdd.Text.Trim();
        if (title.Length == 0 || ListPicker.SelectedValue is not Guid listId)
        {
            return null;
        }

        var task = await _tasks.Repository.AddTaskAsync(listId, title, inMyDay: true);
        QuickAdd.Text = string.Empty;
        await ReloadAsync();
        return task;
    }

    /// <summary>
    /// La varita desglosa la tarea seleccionada. Si no hay ninguna pero si texto escrito, primero
    /// la crea: escribir "Organizar la mudanza" y pulsar la varita es el gesto natural.
    /// </summary>
    private async void OnBreakdownClick(object sender, RoutedEventArgs e)
    {
        var task = TaskList.SelectedItem is TaskRow row
            ? await _tasks.Repository.GetTaskAsync(row.Id)
            : await AddTaskAsync();

        if (task is null)
        {
            return;
        }

        BreakdownButton.IsEnabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var proposal = await _tasks.ProposeBreakdownAsync(task, cts.Token);

            if (!proposal.HasSomethingNew)
            {
                ShowToast(proposal.AlreadyPresent > 0 ? Localization.Loc.Get("MagicAllPresent") : Localization.Loc.Get("MagicNothing"));
                return;
            }

            // Se propone y se pregunta: los pasos no se incorporan sin que el usuario los vea.
            var detail = "• " + string.Join("\n• ", proposal.Steps);
            if (proposal.AlreadyPresent > 0)
                detail += $"\n\n({proposal.AlreadyPresent} ya estaban y se han descartado)";

            var accepted = MessageBox.Show(this, detail, $"{Localization.Loc.Get("MagicSteps")} · {proposal.Source}",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

            if (!accepted)
                return;

            var (steps, celebration) = await _tasks.ApplyBreakdownAsync(task, proposal.Steps);
            await ReloadAsync();

            if (celebration is not null)
                Celebrate(celebration);
            else
                ShowToast($"{steps.Count} pasos añadidos");
        }
        finally
        {
            BreakdownButton.IsEnabled = true;
        }
    }

    private async void OnTaskChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: Guid id })
        {
            return;
        }

        var task = await _tasks.Repository.GetTaskAsync(id);
        if (task is null || task.IsDone)
        {
            return;
        }

        var celebration = await _tasks.CompleteTaskAsync(task);
        await ReloadAsync();

        if (celebration is not null)
        {
            Celebrate(celebration);
        }
    }

    private async void OnTaskUnchecked(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: Guid id })
        {
            return;
        }

        var task = await _tasks.Repository.GetTaskAsync(id);
        if (task is { IsDone: true })
        {
            await _tasks.UncompleteTaskAsync(task);
            await ReloadAsync();
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnCalendarClick(object sender, RoutedEventArgs e) =>
        CalendarRequested?.Invoke(this, EventArgs.Empty);

    private void OnMainClick(object sender, RoutedEventArgs e) =>
        MainRequested?.Invoke(this, EventArgs.Empty);

    // -----------------------------------------------------------------------
    // Celebracion
    // -----------------------------------------------------------------------

    private void Celebrate(Celebration celebration)
    {
        var origin = new Point(ActualWidth / 2, ActualHeight * 0.35);
        Confetti.Burst(origin, celebration.Combo);

        var text = celebration.LeveledUp
            ? Localization.Loc.Format("LevelUp", celebration.Level, celebration.Xp)
            : celebration.IsCombo
                ? $"+{celebration.Xp} XP · ¡Racha x{celebration.Combo:0.#}!"
                : $"+{celebration.Xp} XP";

        ShowToast(text);

        if (celebration.Unlocked is { } unlocked)
        {
            ShowToast(Localization.Loc.Format("UnlockedItem", unlocked.Name));
        }
    }

    private void ShowToast(string text)
    {
        XpToastLabel.Text = text;

        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1400))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1800))));
        XpToast.BeginAnimation(OpacityProperty, animation);
    }

    // -----------------------------------------------------------------------

    private sealed record ListOption(Guid Id, string Display);

    /// <summary>Fila de la lista. Se aplana aqui lo que el XAML necesita mostrar.</summary>
    // -----------------------------------------------------------------------
    // Ordenar arrastrando
    // -----------------------------------------------------------------------

    private Point _dragStart;
    private TaskRow? _dragging;

    private void OnTaskDragStart(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragging = RowUnder(e.OriginalSource as DependencyObject);
    }

    /// <summary>
    /// Empieza el arrastre solo cuando el raton se ha movido lo que Windows considera un arrastre
    /// de verdad. Sin ese umbral, el temblor de la mano al hacer clic en la casilla de completar se
    /// interpretaria como un arrastre y la tarea se movria de sitio sola.
    /// </summary>
    private void OnTaskDragMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var moved = e.GetPosition(null) - _dragStart;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(TaskList, _dragging, DragDropEffects.Move);
    }

    private void OnTaskDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TaskRow)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnTaskDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(TaskRow)) is not TaskRow moved)
        {
            return;
        }

        var target = RowUnder(e.OriginalSource as DependencyObject);
        var from = _rows.IndexOf(moved);

        // Soltar fuera de cualquier fila deja la tarea al final; es lo que se espera al arrastrar
        // hacia el hueco de abajo.
        var to = target is null ? _rows.Count - 1 : _rows.IndexOf(target);

        _dragging = null;

        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        _rows.Move(from, to);
        await _tasks.Repository.ReorderTasksAsync([.. _rows.Select(r => r.Id)]);
    }

    /// <summary>Fila del listado que hay bajo un elemento visual cualquiera de la plantilla.</summary>
    private static TaskRow? RowUnder(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
        {
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return (source as ListBoxItem)?.DataContext as TaskRow;
    }

    public sealed class TaskRow
    {
        public TaskRow(TaskItem task, string listName)
        {
            Id = task.Id;
            Title = task.Title;
            IsDone = task.IsDone;
            ListName = listName;
            StepsCaption = task.StepCount > 0 ? $"{task.StepsDone}/{task.StepCount} pasos" : string.Empty;
        }

        public Guid Id { get; }

        public string Title { get; }

        public bool IsDone { get; }

        public string ListName { get; }

        public string StepsCaption { get; }

        public Visibility StepsVisibility => StepsCaption.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
