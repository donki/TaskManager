using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TaskManager.Core.Data;
using TaskManager.Core.Models;
using TaskManager.Core.Services;

namespace TaskManager.Desktop;

/// <summary>
/// Ventana principal de Windows: «Mis tareas» y «Mis listas».
/// </summary>
/// <remarks>
/// <para>Son <b>las mismas dos pantallas que en Android</b>, con los mismos filtros
/// (<see cref="TaskFilters"/>) y el mismo detalle de tarea
/// (<see cref="TaskDetailWindow"/>). Antes esto era otra aplicacion: no habia forma de ver todas las
/// tareas juntas ni de abrir una para editarla, y encima llevaba un gremio y una bandeja de correo
/// que el movil enseñaba en otro sitio. Cambiar de aparato obligaba a reaprender.</para>
///
/// <para>El gremio y los grupos se han quitado, y el correo esta oculto tras
/// <see cref="FeatureOptions.MailEnabled"/>.</para>
/// </remarks>
public partial class MainWindow : Window
{
    private readonly TaskService _tasks;
    private readonly SettingsService _settings;

    /// <summary>Quien sabe hablar con el servidor. Null si la aplicacion va solo en local.</summary>
    private readonly SyncCoordinator? _syncing;

    private readonly ObservableCollection<ListRow> _lists = [];
    private readonly ObservableCollection<TaskRow> _listTasks = [];
    private readonly ObservableCollection<TaskRow> _allTasks = [];

    private readonly Dictionary<Guid, string> _listNames = [];

    private TaskFilter _filter = TaskFilters.Default;

    private Point _dragStart;
    private TaskRow? _dragging;
    private ListBox? _dragList;
    private string? _activeTag;
    private string? _search;
    private string? _listSearch;
    private Guid _selectedList;

    public MainWindow(TaskService tasks, SettingsService settings, SyncCoordinator? syncing = null)
    {
        InitializeComponent();

        _tasks = tasks;
        _settings = settings;
        _syncing = syncing;

        ListsBox.ItemsSource = _lists;
        ListTasksBox.ItemsSource = _listTasks;
        AllTasksBox.ItemsSource = _allTasks;

        Services.ThemeManager.StyleTitleBar(this);

        BuildFilters();
    }

    private static string T(string key) => Localization.Loc.Get(key);

    private static string F(string key, params object[] args) => Localization.Loc.Format(key, args);

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        await ReloadListsAsync();
        await ReloadAllTasksAsync();
        await ReloadBoardAsync();
    }

    /// <summary>Vuelve a leerlo todo. Lo llama el arranque cuando la sincronizacion trae algo.</summary>
    public async Task ReloadAsync()
    {
        await ReloadListsAsync();
        await ReloadListTasksAsync();
        await ReloadAllTasksAsync();
        await ReloadBoardAsync();
    }

    // =======================================================================
    // Mis tareas
    // =======================================================================

    /// <summary>
    /// Las pastillas se pintan una vez; elegir una solo cambia cual esta marcada. Se usa
    /// <see cref="ToggleButton"/> porque el estado «puesto / no puesto» ya viene de serie.
    /// </summary>
    private void BuildFilters()
    {
        foreach (var filter in TaskFilters.All)
        {
            var chip = new ToggleButton
            {
                Content = T(TaskFilters.KeyOf(filter)),
                Style = (Style)FindResource("Chip"),
                IsChecked = filter == _filter,
                Tag = filter,
            };

            chip.Checked += async (s, _) =>
            {
                _filter = (TaskFilter)((ToggleButton)s).Tag;

                foreach (var other in FilterBox.Children.OfType<ToggleButton>())
                {
                    if (!ReferenceEquals(other, s))
                    {
                        other.IsChecked = false;
                    }
                }

                await ReloadAllTasksAsync();
            };

            // Sin esto se podria dejar la fila sin ningun filtro puesto, y entonces la pantalla no
            // sabria que enseñar. Volver a pulsar el que ya esta no lo apaga.
            chip.Unchecked += (s, _) =>
            {
                if (!FilterBox.Children.OfType<ToggleButton>().Any(c => c.IsChecked == true))
                {
                    ((ToggleButton)s).IsChecked = true;
                }
            };

            FilterBox.Children.Add(chip);
        }
    }

    private async Task ReloadAllTasksAsync()
    {
        await RefreshTagFilterAsync();

        var tasks = await _tasks.Repository.GetAllTasksAsync(_filter, _activeTag, _search);

        _allTasks.Clear();
        foreach (var task in tasks)
        {
            _allTasks.Add(new TaskRow(task, _listNames.GetValueOrDefault(task.ListId, string.Empty)));
        }

        FilterLabel.Text = _activeTag switch
        {
            null => T(TaskFilters.KeyOf(_filter)),
            TaskRepository.NoTag => $"{T(TaskFilters.KeyOf(_filter))}  ·  {T("NoTagFilter")}",
            _ => $"{T(TaskFilters.KeyOf(_filter))}  ·  #{_activeTag}",
        };
        SummaryLabel.Text = tasks.Count == 1 ? T("TaskCountOne") : F("TaskCount", tasks.Count);
        NoTasksLabel.Text = string.IsNullOrWhiteSpace(_search)
            ? T("NoTasksForFilter")
            : F("NoSearchResults", _search);
        NoTasksLabel.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Pinta las etiquetas que hay en uso. Se rehace en cada recarga porque la ultima tarea que
    /// llevaba una etiqueta puede haberse borrado, y entonces esa etiqueta ya no existe.
    /// </summary>
    private async Task RefreshTagFilterAsync()
    {
        var tags = await _tasks.Repository.GetTagsAsync();

        TagFilterScroll.Visibility = tags.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        TagFilterBox.Children.Clear();

        if (tags.Count == 0)
        {
            _activeTag = null;
            return;
        }

        if (_activeTag is not null && _activeTag != TaskRepository.NoTag &&
            !tags.Contains(_activeTag, StringComparer.CurrentCultureIgnoreCase))
        {
            _activeTag = null;
        }

        TagFilterBox.Children.Add(BuildTagChip(T("AllTags"), null));
        TagFilterBox.Children.Add(BuildTagChip(T("NoTagFilter"), TaskRepository.NoTag));

        foreach (var tag in tags)
        {
            TagFilterBox.Children.Add(BuildTagChip($"#{tag}", tag));
        }
    }

    private ToggleButton BuildTagChip(string text, string? tag)
    {
        var chip = new ToggleButton
        {
            Content = text,
            Style = (Style)FindResource("Chip"),
            IsChecked = string.Equals(_activeTag, tag, StringComparison.CurrentCultureIgnoreCase),
            Tag = tag,
        };

        chip.Click += async (_, _) =>
        {
            _activeTag = tag;
            await ReloadAllTasksAsync();
        };

        return chip;
    }

    /// <summary>
    /// Sincroniza y vuelve a leerlo todo, a peticion.
    /// </summary>
    /// <remarks>
    /// El coordinador ya sincroniza solo —al abrir, al cambiar algo y cada pocos minutos— y la
    /// ventana se relee cuando eso trae novedades. Pero entre ronda y ronda pueden pasar minutos, y
    /// quien acaba de escribir una tarea en el movil quiere verla <b>ahora</b>, no cuando toque.
    /// </remarks>
    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        // Los dos botones de refrescar (el de «Mis tareas» y el de «Mis listas») llaman aqui y los
        // dos se apagan: da igual desde cual se pidiera, lo que se esta refrescando es lo mismo.
        RefreshButton.IsEnabled = false;
        ListsRefreshButton.IsEnabled = false;

        try
        {
            if (_syncing is not null)
            {
                await _syncing.SyncNowAsync();
            }

            await ReloadAsync();
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            ListsRefreshButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Busca al escribir, en todo el texto de la tarea (titulo, notas, etiquetas, pasos y adjuntos).
    /// </summary>
    /// <remarks>
    /// Sin boton de buscar y sin esperar a pulsar Enter: la lista es local y la consulta es
    /// instantanea, asi que hacer pulsar un boton solo añadiria un paso.
    /// </remarks>
    private async void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text;
        await ReloadAllTasksAsync();
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e) => SearchBox.Clear();

    private async void OnListSearchChanged(object sender, TextChangedEventArgs e)
    {
        _listSearch = ListSearchBox.Text;
        await ReloadListTasksAsync();
    }

    private void OnClearListSearchClick(object sender, RoutedEventArgs e) => ListSearchBox.Clear();

    private async void OnQuickAddClick(object sender, RoutedEventArgs e) => await QuickAddAsync();

    private async void OnQuickAddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await QuickAddAsync();
        }
    }

    private async Task QuickAddAsync()
    {
        var title = QuickAddBox.Text.Trim();
        if (title.Length == 0)
        {
            return;
        }

        // Sin ninguna lista todavia, se crea la de siempre en vez de pedirla: escribir una tarea no
        // puede acabar en un formulario.
        var listId = _lists.FirstOrDefault()?.Id
            ?? (await _tasks.Repository.GetOrCreateDefaultListAsync(T("DefaultListName"))).Id;

        await _tasks.Repository.AddTaskAsync(listId, title);
        QuickAddBox.Text = string.Empty;

        await ReloadListsAsync();
        await ReloadAllTasksAsync();
    }

    // =======================================================================
    // Mis listas
    // =======================================================================

    private async Task ReloadListsAsync()
    {
        var selected = _selectedList;

        _lists.Clear();
        _listNames.Clear();

        foreach (var list in await _tasks.Repository.GetPrivateListsAsync())
        {
            var tasks = await _tasks.Repository.GetTasksAsync(list.Id);
            var pending = tasks.Count(t => !t.IsDone);

            _listNames[list.Id] = list.Name;
            _lists.Add(new ListRow(list.Id, list.Name,
                pending == 1 ? T("OnePending") : F("ManyPending", pending)));
        }

        if (_lists.Count == 0)
        {
            _selectedList = Guid.Empty;
            _listTasks.Clear();
            return;
        }

        // Se conserva la lista elegida al recargar: perderla en cada refresco saca al usuario de
        // donde estaba trabajando.
        var keep = _lists.FirstOrDefault(l => l.Id == selected) ?? _lists[0];
        ListsBox.SelectedItem = keep;
        _selectedList = keep.Id;

        await ReloadListTasksAsync();
    }

    private async void OnListSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ListsBox.SelectedItem is ListRow row)
        {
            _selectedList = row.Id;
            await ReloadListTasksAsync();
        }
    }

    private async Task ReloadListTasksAsync()
    {
        _listTasks.Clear();

        if (_selectedList == Guid.Empty)
        {
            return;
        }

        foreach (var task in await _tasks.Repository.GetTasksAsync(_selectedList, search: _listSearch))
        {
            _listTasks.Add(new TaskRow(task, string.Empty));
        }
    }

    private async void OnNewListClick(object sender, RoutedEventArgs e)
    {
        var name = Prompt.Ask(this, T("NewListTitle"), T("ListNamePlaceholder"));
        if (!string.IsNullOrWhiteSpace(name))
        {
            await _tasks.Repository.CreateListAsync(name);
            await ReloadListsAsync();
        }
    }

    /// <summary>
    /// Cambia el nombre de una lista. Se llega por el lapiz o por doble clic sobre ella.
    /// </summary>
    /// <remarks>
    /// Faltaba: se podian crear y borrar listas, pero no renombrarlas — en el movil si se podia
    /// desde el detalle de la lista. Una lista mal llamada solo se podia arreglar creando otra y
    /// mudando las tareas a mano.
    /// </remarks>
    private async void OnRenameListClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: Guid id })
        {
            await RenameListAsync(id);
        }
    }

    private async void OnListDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ListsBox.SelectedItem is ListRow row)
        {
            await RenameListAsync(row.Id);
        }
    }

    private async Task RenameListAsync(Guid id)
    {
        var list = await _tasks.Repository.GetListAsync(id);
        if (list is null)
        {
            return;
        }

        var name = Prompt.Ask(this, T("RenameListTitle"), T("ListNamePlaceholder"), list.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == list.Name)
        {
            return;
        }

        list.Name = name.Trim();
        await _tasks.Repository.UpdateListAsync(list);

        await ReloadAsync();
    }

    /// <summary>
    /// Borra la lista y todo lo que hay dentro, preguntando antes. El aviso dice cuantas tareas se
    /// lleva por delante: «se borrara la lista» a secas esconde justo lo que importa.
    /// </summary>
    private async void OnDeleteListClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id })
        {
            return;
        }

        var list = await _tasks.Repository.GetListAsync(id);
        if (list is null)
        {
            return;
        }

        var tasks = await _tasks.Repository.GetTasksAsync(list.Id);
        Guid? moveTo = null;

        if (tasks.Count > 0)
        {
            // Con tareas dentro no se pregunta si borrar, sino a donde van: borrarlas de calle
            // seria llevarse por delante trabajo que el usuario no ha dicho de tirar.
            var others = _lists.Where(l => l.Id != id).Select(l => (l.Name, l.Id)).ToList();
            if (others.Count == 0)
            {
                // No hay otra lista a la que mandarlas: solo cabe borrarlas con ella.
                if (!Controls.ModernDialog.Confirm(
                        this, T("DeleteListTitle"), F("DeleteListMessage", list.Name), danger: true))
                {
                    return;
                }
            }
            else
            {
                moveTo = Controls.ModernDialog.Choose(
                    this,
                    T("MoveTasksTitle"),
                    tasks.Count == 1
                        ? F("MoveTasksOne", list.Name)
                        : F("MoveTasksMessage", list.Name, tasks.Count),
                    others,
                    T("DeleteWithList"),
                    out var cancelled);

                if (cancelled)
                {
                    return;
                }
            }
        }
        else if (!Controls.ModernDialog.Confirm(
                     this, T("DeleteListTitle"), F("DeleteListMessage", list.Name), danger: true))
        {
            return;
        }

        await _tasks.Repository.DeleteListAsync(list, moveTo);

        // La lista borrada era la que estaba puesta: se suelta para que la recarga elija otra.
        if (_selectedList == id)
        {
            _selectedList = Guid.Empty;
        }

        await ReloadAsync();
    }

    private async void OnAddTaskClick(object sender, RoutedEventArgs e) => await AddTaskAsync();

    private async void OnNewTaskKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await AddTaskAsync();
        }
    }

    private async Task AddTaskAsync()
    {
        var title = NewTaskBox.Text.Trim();
        if (title.Length == 0 || _selectedList == Guid.Empty)
        {
            return;
        }

        await _tasks.Repository.AddTaskAsync(_selectedList, title);
        NewTaskBox.Text = string.Empty;

        await ReloadListTasksAsync();
        await ReloadListsAsync();
        await ReloadAllTasksAsync();
    }

    // =======================================================================
    // Gremio
    // =======================================================================

    /// <summary>
    /// Nivel, experiencia y racha. Es la misma pantalla que en Android, con los mismos textos.
    /// </summary>
    private async Task ReloadBoardAsync()
    {
        var board = await _tasks.GetBoardAsync();

        BoardLevel.Text = F("Level", board.Level);
        BoardProgress.Value = board.ProgressInLevel;
        BoardToNext.Text = F("ToNextLevel", board.XpToNextLevel, board.Level + 1);
        BoardXp.Text = F("XpTotal", board.TotalXp);

        BoardStreak.Text = board.CurrentStreak switch
        {
            0 => T("NoStreak"),
            1 => T("StreakOne"),
            _ => F("StreakMany", board.CurrentStreak),
        };

        BoardNextUnlock.Text = board.NextUnlock is { } next
            ? F("NextUnlock", next.Name, next.Level)
            : T("AllUnlocked");
    }

    // =======================================================================
    // Arrastrar para reordenar
    // =======================================================================

    /// <summary>
    /// Reordenar arrastrando, igual que en el panel rapido y que en el movil.
    /// </summary>
    /// <remarks>
    /// <para>Las dos listas comparten los mismos manejadores: el que se este usando se sabe por el
    /// <c>sender</c>, asi que no hacen falta dos juegos identicos.</para>
    ///
    /// <para>El arrastre no arranca al pulsar sino cuando el raton se ha movido lo bastante
    /// (<see cref="SystemParameters.MinimumHorizontalDragDistance"/>). Si arrancara al pulsar,
    /// marcar una casilla o abrir el detalle se convertiria en un arrastre accidental.</para>
    /// </remarks>
    private void OnTaskDragStart(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragList = sender as ListBox;
        _dragging = RowUnder(e.OriginalSource as DependencyObject);
    }

    private void OnTaskDragMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null || _dragList is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        // Con Ctrl o Mayus pulsados no se arrastra: esos son los gestos de marcar varias.
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        var moved = e.GetPosition(null) - _dragStart;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(_dragList, _dragging, DragDropEffects.Move);
    }

    private void OnTaskDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TaskRow)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnTaskDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(TaskRow)) is not TaskRow moved || sender is not ListBox list)
        {
            return;
        }

        var rows = ReferenceEquals(list, AllTasksBox) ? _allTasks : _listTasks;
        var target = RowUnder(e.OriginalSource as DependencyObject);

        var from = rows.IndexOf(moved);

        // Soltar fuera de cualquier fila deja la tarea al final: es lo que se espera al arrastrar
        // hacia el hueco de abajo.
        var to = target is null ? rows.Count - 1 : rows.IndexOf(target);

        _dragging = null;
        _dragList = null;

        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        rows.Move(from, to);

        // No se recarga: la lista ya esta como el usuario la ha dejado, y repintarla justo al
        // soltar da un parpadeo.
        await _tasks.Repository.ReorderTasksAsync([.. rows.Select(r => r.Id)]);
    }

    /// <summary>La fila sobre la que esta el raton, subiendo desde lo que se pulso.</summary>
    private static TaskRow? RowUnder(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
        {
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return (source as ListBoxItem)?.Content as TaskRow;
    }

    // =======================================================================
    // Acciones sobre una tarea
    // =======================================================================
    // Varias tareas a la vez
    // =======================================================================
    //
    // Se marca con Ctrl+clic o Mayus+clic, como en cualquier lista de Windows, y la barra aparece
    // sola en cuanto hay mas de una. Con una sola marcada no aparece: seleccionar es lo que pasa al
    // hacer clic en cualquier fila, y una barra saltando en cada clic estorbaria mas de lo que ayuda
    // — para una tarea suelta ya esta el detalle.

    /// <summary>La lista donde se marco lo ultimo. Hay dos, y las dos comparten esta barra.</summary>
    private ListBox? _selectionBox;

    private void OnTaskSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox box)
        {
            return;
        }

        _selectionBox = box;

        var mine = ReferenceEquals(box, AllTasksBox);
        var bar = mine ? SelectionBar : ListSelectionBar;
        var label = mine ? SelectionLabel : ListSelectionLabel;

        var count = box.SelectedItems.Count;
        bar.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        label.Text = F("SelectionCount", count);

        // Marcar en una lista suelta lo marcado en la otra: si no, dos barras a la vez y ninguna
        // pista de sobre cual actuarian los botones.
        var other = mine ? ListTasksBox : AllTasksBox;
        if (count > 1 && other.SelectedItems.Count > 0)
        {
            other.UnselectAll();
        }
    }

    private List<Guid> SelectedIds() =>
        _selectionBox is null
            ? []
            : [.. _selectionBox.SelectedItems.OfType<TaskRow>().Select(r => r.Id)];

    private async void OnBulkDoneClick(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds();
        if (ids.Count == 0)
        {
            return;
        }

        await _tasks.CompleteManyAsync(ids);
        await ReloadAsync();
    }

    private async void OnBulkPendingClick(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds();
        if (ids.Count == 0)
        {
            return;
        }

        await _tasks.UncompleteManyAsync(ids);
        await ReloadAsync();
    }

    private async void OnBulkPriorityOnClick(object sender, RoutedEventArgs e) => await SetPriorityAsync(true);

    private async void OnBulkPriorityOffClick(object sender, RoutedEventArgs e) => await SetPriorityAsync(false);

    private async Task SetPriorityAsync(bool priority)
    {
        var ids = SelectedIds();
        if (ids.Count == 0)
        {
            return;
        }

        await _tasks.Repository.SetPriorityAsync(ids, priority);
        await ReloadAsync();
    }

    /// <summary>
    /// Pone una etiqueta a todo lo marcado. Se ofrecen las que ya existen antes que escribir una
    /// nueva, que es lo que evita acabar con "casa", "Casa" y "casa " como tres etiquetas.
    /// </summary>
    private async void OnBulkTagClick(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds();
        if (ids.Count == 0)
        {
            return;
        }

        var tag = Controls.ModernDialog.PickOrType(
            this,
            T("BulkTag"),
            F("BulkTagMessage", ids.Count),
            await _tasks.Repository.GetTagsAsync(),
            T("BulkTagHint"),
            T("BulkTag"));

        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        await _tasks.Repository.AddTagAsync(ids, tag);
        await ReloadAsync();
    }

    private async void OnBulkMoveClick(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds();
        if (ids.Count == 0)
        {
            return;
        }

        var lists = _lists.Select(l => (l.Name, l.Id)).ToList();
        if (lists.Count == 0)
        {
            return;
        }

        var target = Controls.ModernDialog.Pick(
            this, T("BulkMove"), F("BulkMoveMessage", ids.Count), lists, T("BulkMove"));

        if (target is not { } listId)
        {
            return;
        }

        await _tasks.Repository.MoveTasksAsync(ids, listId);
        await ReloadAsync();
    }

    private async void OnBulkDeleteClick(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds();
        if (ids.Count == 0)
        {
            return;
        }

        if (!Controls.ModernDialog.Confirm(
                this, T("BulkDelete"), F("BulkDeleteConfirm", ids.Count), danger: true))
        {
            return;
        }

        await _tasks.Repository.DeleteTasksAsync(ids);
        await ReloadAsync();
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e) => _selectionBox?.UnselectAll();

    // =======================================================================

    private async void OnTaskToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: Guid id })
        {
            return;
        }

        var task = await _tasks.Repository.GetTaskAsync(id);
        if (task is null)
        {
            return;
        }

        // Completar celebra y suma XP; deshacer lo devuelve sin castigar.
        if (task.IsDone)
        {
            await _tasks.UncompleteTaskAsync(task);
        }
        else
        {
            await _tasks.CompleteTaskAsync(task);
        }

        await ReloadAsync();
    }

    /// <summary>
    /// Doble clic en una fila: lo mismo que el lapiz. Es el gesto que espera cualquiera en una lista
    /// de escritorio, y tener que apuntar a un icono de 30 pixeles para editar sobraba.
    /// </summary>
    /// <remarks>
    /// <para>Se escucha en la <b>lista</b> y no en cada fila. Se intento con
    /// <c>MouseLeftButtonDown</c> sobre el borde de cada tarjeta mirando <c>ClickCount</c>, y no
    /// llegaba a dispararse; <see cref="Control.MouseDoubleClick"/> es el evento que WPF trae para
    /// esto y no depende de contar pulsaciones a mano.</para>
    ///
    /// <para>La fila se averigua subiendo desde lo que se pulso: asi da igual si el doble clic cayo
    /// sobre el titulo, sobre la fecha o sobre el hueco de la tarjeta.</para>
    /// </remarks>
    private async void OnRowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;

        while (source is not null and not ListBoxItem)
        {
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        if (source is ListBoxItem { Content: TaskRow row })
        {
            await OpenTaskAsync(row.Id);
        }
    }

    /// <summary>Abre el detalle, que es donde estan las notas, las fechas, la repeticion y los pasos.</summary>
    private async void OnOpenTaskClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
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

        var window = new TaskDetailWindow(_tasks, task)
        {
            Owner = this,
            Icon = Services.TrayIconHost.CreateWindowIcon(),
        };

        window.ShowDialog();

        if (window.Changed)
        {
            await ReloadAsync();
        }
    }

    // =======================================================================

    private sealed record ListRow(Guid Id, string Name, string Caption);

    /// <summary>Fila de tarea lista para pintar, con el mismo contenido que la del movil.</summary>
    private sealed record TaskRow
    {
        public TaskRow(TaskItem task, string listName)
        {
            Id = task.Id;

            // La estrella delante en vez de una columna aparte: la fila ya tiene casilla, texto y
            // lapiz, y una cuarta pieza vacia en casi todas las filas solo estrecharia el titulo.
            Title = task.IsPriority ? "★ " + task.Title : task.Title;
            IsDone = task.IsDone;

            var parts = new List<string>();

            if (task.PlannedFor is { } planned)
            {
                parts.Add(Localization.Loc.Format("PlannedShort", planned.ToString("d MMM")));
            }

            if (task.DueAt is { } due)
            {
                parts.Add(Localization.Loc.Format("DueShort", due.ToString("d MMM")));
            }

            if (task.StepCount > 0)
            {
                parts.Add($"{task.StepsDone}/{task.StepCount}");
            }

            if (task.TagList.Count > 0)
            {
                parts.Add("#" + string.Join("  #", task.TagList));
            }

            if (listName.Length > 0)
            {
                parts.Add(listName);
            }

            Caption = string.Join("  ·  ", parts);
        }

        public Guid Id { get; }

        public string Title { get; }

        public bool IsDone { get; }

        public string Caption { get; }

        public Visibility CaptionVisibility =>
            Caption.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        public TextDecorationCollection? Decoration =>
            IsDone ? TextDecorations.Strikethrough : null;

        public double Opacity => IsDone ? 0.55 : 1.0;
    }
}

/// <summary>Cuadro de una sola linea: se usa para pedir el nombre de una lista.</summary>
public static class Prompt
{
    /// <param name="initial">
    /// Lo que aparece ya escrito. Al renombrar algo se pasa el nombre actual: obligar a reescribirlo
    /// entero para cambiarle una letra es de las cosas que mas cansan de una interfaz.
    /// </param>
    public static string? Ask(Window owner, string title, string hint, string? initial = null)
    {
        var box = new TextBox
        {
            Style = (Style)Application.Current.FindResource("Field"),
            Margin = new Thickness(0, 0, 0, 12),
            Text = initial ?? string.Empty,
        };

        var ok = new Button
        {
            Style = (Style)Application.Current.FindResource("IconButton"),
            Content = "",
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = hint,
            Margin = new Thickness(0, 0, 0, 8),
            Style = (Style)Application.Current.FindResource("HintText"),
        });
        panel.Children.Add(box);
        panel.Children.Add(ok);

        var window = new Window
        {
            Title = title,
            Content = panel,
            Owner = owner,
            Width = 340,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            // Fuera de la barra de tareas: es un cuadro de una linea que pertenece a su ventana,
            // no un sitio al que volver desde la barra.
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("PageBackground"),
        };

        Services.ThemeManager.StyleTitleBar(window);

        ok.Click += (_, _) => window.DialogResult = true;
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                window.DialogResult = true;
            }
        };

        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();   // Escribir de cero sigue siendo un gesto: teclear encima.
        };

        return window.ShowDialog() == true ? box.Text.Trim() : null;
    }
}
