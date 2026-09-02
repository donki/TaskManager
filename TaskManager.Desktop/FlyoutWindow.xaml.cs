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

    /// <summary>Quien habla con el servidor. Puede faltar: sin Supabase configurado no hay ninguno.</summary>
    private readonly SyncCoordinator? _syncing;
    private readonly ObservableCollection<TaskRow> _rows = [];

    /// <summary>Etiqueta por la que se esta acotando, o null si se ven todas.</summary>
    private string? _activeTag;
    private string? _search;
    private readonly Dictionary<Guid, string> _listNames = [];

    private bool _closingForReal;

    public FlyoutWindow(TaskService tasks, SettingsService settings, SyncCoordinator? syncing = null)
    {
        InitializeComponent();

        _tasks = tasks;
        _settings = settings;
        _syncing = syncing;
        TaskList.ItemsSource = _rows;

        // La fecha va en el idioma elegido: en ingles «de» sobra y el dia de la semana cambia.
        DateLabel.Text = DateTime.Now.ToString(
            Localization.Loc.Get("DatePattern"),
            System.Globalization.CultureInfo.GetCultureInfo(Localization.Loc.Language));
        QuickAdd.Text = string.Empty;
    }

    /// <summary>
    /// Refrescar: habla con el servidor y vuelve a pintar.
    /// </summary>
    /// <remarks>
    /// El panel rapido es donde mas se mira y era el unico sitio sin este boton: para saber si habia
    /// algo nuevo del movil habia que abrir la ventana grande. Es el mismo de «Mis tareas», y espera
    /// a que la sincronizacion termine de verdad antes de repintar.
    /// </remarks>
    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;

        try
        {
            if (_syncing is not null)
            {
                await _syncing.RefreshNowAsync();
            }

            await ReloadAsync();
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
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

        // Lo que queda por hacer, de todas las listas. Antes era «Mi Día» —solo lo marcado
        // para hoy—, que ya no existe: obligaba a acordarse de marcar cada tarea para verla.
        await RefreshTagFilterAsync();

        // Solo lo pendiente: el panel rapido es para lo que queda por hacer, no un archivo.
        var tasks = await _tasks.Repository.GetAllTasksAsync(
            TaskManager.Core.Models.TaskFilter.Pending, _activeTag, _search);
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

    /// <summary>Da de alta la tarea y abre su detalle.</summary>
    /// <remarks>
    /// La caja de arriba solo puede recoger el titulo, y una tarea recien escrita casi siempre
    /// necesita algo mas: de que lista cuelga y con que etiqueta. Antes eso obligaba a escribirla,
    /// buscarla en la lista y abrirla. Se abre sola: escribir sigue costando lo mismo y quien no
    /// quiera tocar nada mas, cierra.
    /// </remarks>
    private async Task<TaskItem?> AddTaskAsync()
    {
        var title = QuickAdd.Text.Trim();
        if (title.Length == 0 || ListPicker.SelectedValue is not Guid listId)
        {
            return null;
        }

        var task = await _tasks.Repository.AddTaskAsync(listId, title);
        QuickAdd.Text = string.Empty;
        await ReloadAsync();

        // OpenTaskAsync esconde el panel antes de abrir el detalle, que es lo que hace falta: el
        // panel se cierra al perder el foco y si no desapareceria por debajo del detalle.
        await OpenTaskAsync(task.Id);
        return task;
    }

    /// <summary>Busca al escribir, en todo el texto de la tarea.</summary>
    private async void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _search = SearchBox.Text;
        await ReloadAsync();
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e) => SearchBox.Clear();

    /// <summary>
    /// Pinta las etiquetas que hay en uso, para acotar el panel sin salir de el.
    /// </summary>
    /// <remarks>
    /// Se rehace en cada recarga porque la ultima tarea que llevaba una etiqueta puede haberse
    /// completado, y entonces esa etiqueta ya no tiene nada pendiente detras.
    /// </remarks>
    private async Task RefreshTagFilterAsync()
    {
        var tags = await _tasks.Repository.GetTagsAsync();

        TagFilterScroll.Visibility = tags.Count == 0
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

        TagFilterBox.Children.Clear();

        if (tags.Count == 0)
        {
            _activeTag = null;
            return;
        }

        if (_activeTag is not null && _activeTag != TaskManager.Core.Data.TaskRepository.NoTag &&
            !tags.Contains(_activeTag, StringComparer.CurrentCultureIgnoreCase))
        {
            _activeTag = null;
        }

        TagFilterBox.Children.Add(BuildTagChip(Localization.Loc.Get("AllTags"), null));
        TagFilterBox.Children.Add(BuildTagChip(
            Localization.Loc.Get("NoTagFilter"), TaskManager.Core.Data.TaskRepository.NoTag));
        foreach (var tag in tags)
        {
            TagFilterBox.Children.Add(BuildTagChip($"#{tag}", tag));
        }
    }

    private System.Windows.Controls.Primitives.ToggleButton BuildTagChip(string text, string? tag)
    {
        var chip = new System.Windows.Controls.Primitives.ToggleButton
        {
            Content = text,
            Style = (System.Windows.Style)FindResource("Chip"),
            IsChecked = string.Equals(_activeTag, tag, StringComparison.CurrentCultureIgnoreCase),
        };

        chip.Click += async (_, _) =>
        {
            _activeTag = tag;
            await ReloadAsync();
        };

        return chip;
    }

    /// <summary>
    /// Abre el detalle de la tarea desde el panel rapido.
    /// </summary>
    /// <remarks>
    /// <para>El panel es para lo rapido —marcar hecho y escribir una tarea nueva—, pero hasta ahora
    /// no habia forma de <b>abrir</b> nada desde el: para cambiar una fecha o mirar los pasos
    /// tocaba ir a la ventana principal.</para>
    ///
    /// <para>Se abre por el lapiz y por doble clic. El lapiz no sobra: esta lista se reordena
    /// arrastrando, y el arrastre y el doble clic se pisan con facilidad.</para>
    /// </remarks>
    private async void OnOpenTaskClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: Guid id })
        {
            await OpenTaskAsync(id);
        }
    }

    private async void OnTaskDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;

        while (source is not null and not System.Windows.Controls.ListBoxItem)
        {
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        if (source is System.Windows.Controls.ListBoxItem { Content: { } row }
            && row.GetType().GetProperty("Id")?.GetValue(row) is Guid id)
        {
            await OpenTaskAsync(id);
        }
    }

    private async Task OpenTaskAsync(Guid id)
    {
        var task = await _tasks.Repository.GetTaskAsync(id);
        if (task is null)
        {
            return;
        }

        // El panel se esconde al perder el foco: se esconde antes a proposito, para que no
        // desaparezca por debajo justo cuando se abre el detalle.
        HideFlyout();

        var window = new TaskDetailWindow(_tasks, task)
        {
            Icon = Services.TrayIconHost.CreateWindowIcon(),
        };

        window.ShowDialog();

        if (window.Changed)
        {
            await ReloadAsync();
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
            Title = task.IsPinned ? "📌 " + task.Title : task.Title;
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
