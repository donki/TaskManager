using System.Collections.ObjectModel;
using TaskManager.Core.Data;
using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;
using TaskManager.Mobile.Models;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// El tablero: las mismas tareas de «Mis tareas», repartidas en tres columnas por estado.
/// </summary>
/// <remarks>
/// <para>Es la misma pantalla que en Windows y con las mismas columnas —pendientes, en curso y
/// hechas—, para que cambiar de aparato no obligue a reaprender nada. La lista dice <i>que</i> hay
/// que hacer; el tablero, <i>por donde va</i> cada cosa.</para>
///
/// <para><b>Las tres columnas se ven siempre</b>, tambien con la tableta en vertical: el ancho se
/// reparte a partes iguales en vez de darle a cada columna una medida fija. Con medida fija, en
/// vertical se verian dos y media y habria que deslizar de lado para saber si hay algo en la
/// tercera, que es justo lo que un tablero tiene que contestar de un vistazo.</para>
///
/// <para><b>Aqui el arrastre ordena dentro de la columna</b>, y no cambia de columna como en
/// Windows. En Android los dos gestos serian el mismo —pulsacion larga y mover— y hay que elegir
/// uno. El estado se cambia <b>tocando la tarjeta</b>, que abre la tarea: ahi estan «empezada» y
/// «hecha». En Windows, donde el raton distingue soltar aqui de soltar alli, se hacen las dos
/// cosas: a otra columna cambia el estado, a la suya recoloca.</para>
/// </remarks>
public partial class KanbanPage : ContentPage
{
    private readonly TaskService _tasks;
    private readonly SettingsService _settings;
    private readonly Dictionary<Guid, string> _listNames = [];

    private readonly ObservableCollection<TaskRow> _todo = [];
    private readonly ObservableCollection<TaskRow> _doing = [];
    private readonly ObservableCollection<TaskRow> _done = [];

    /// <summary>
    /// El tablero tiene su propio filtro, como en Windows: alli se arranca en «pendientes», que es
    /// a lo que se viene, y aqui en «todas», porque un tablero con la columna de hechas siempre
    /// vacia no es un tablero.
    /// </summary>
    private TaskFilter _filter = TaskFilter.All;
    private string? _activeTag;
    private string? _search;

    public KanbanPage()
        : this(ServiceHelper.GetRequiredService<TaskService>(),
               ServiceHelper.GetRequiredService<SettingsService>())
    {
    }

    public KanbanPage(TaskService tasks, SettingsService settings)
    {
        InitializeComponent();

        _tasks = tasks;
        _settings = settings;

        TodoView.ItemsSource = _todo;
        DoingView.ItemsSource = _doing;
        DoneView.ItemsSource = _done;

        BuildFilters();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _tasks.InitializeAsync();
        await ReloadAsync();
    }

    // -----------------------------------------------------------------------

    private async Task ReloadAsync()
    {
        await LoadListNamesAsync();
        await RefreshTagFilterAsync();

        var tasks = await _tasks.Repository.GetAllTasksAsync(_filter, _activeTag, _search);

        _todo.Clear();
        _doing.Clear();
        _done.Clear();

        foreach (var task in tasks)
        {
            var row = new TaskRow(task, _listNames.GetValueOrDefault(task.ListId, string.Empty));

            // Hecha manda sobre empezada: una tarea terminada esta terminada aunque quedara algo
            // encendido de antes.
            var column = task.IsDone ? _done : task.InProgress ? _doing : _todo;
            column.Add(row);
        }

        TodoCount.Text = _todo.Count.ToString();
        DoingCount.Text = _doing.Count.ToString();
        DoneCount.Text = _done.Count.ToString();

        var counts = await _tasks.Repository.CountProgressAsync();
        FooterLabel.Text = ProgressCaption.Footer(
            _todo.Count + _doing.Count, counts, Localization.Loc.Instance.Textos);
    }

    private async Task LoadListNamesAsync()
    {
        _listNames.Clear();

        foreach (var list in await _tasks.Repository.GetPrivateListsAsync())
        {
            _listNames[list.Id] = list.Name;
        }

        foreach (var group in await _tasks.Repository.GetGroupsAsync())
        {
            foreach (var list in await _tasks.Repository.GetGroupListsAsync(group.Id))
            {
                _listNames[list.Id] = $"{group.Name} · {list.Name}";
            }
        }
    }

    // -----------------------------------------------------------------------
    // Ordenar dentro de la columna
    // -----------------------------------------------------------------------

    /// <summary>
    /// Arrastrar una tarjeta la coloca donde se deja, dentro de su columna.
    /// </summary>
    /// <remarks>
    /// <para><b>Aqui el arrastre ordena, no cambia de estado</b>, al reves que en Windows. No es un
    /// capricho: en Android los dos serian el mismo gesto —pulsacion larga y mover— y hay que elegir
    /// uno. El estado se cambia tocando la tarjeta, que abre la tarea y ahi esta.</para>
    ///
    /// <para>No se recarga al soltar: la columna ya esta como el usuario la ha dejado, y repintarla
    /// justo al levantar el dedo da un parpadeo. Solo se guarda el orden que ya se ve.</para>
    /// </remarks>
    private async void OnTodoReordered(object? sender, EventArgs e) => await SaveOrderAsync(_todo);

    private async void OnDoingReordered(object? sender, EventArgs e) => await SaveOrderAsync(_doing);

    private async void OnDoneReordered(object? sender, EventArgs e) => await SaveOrderAsync(_done);

    private async Task SaveOrderAsync(ObservableCollection<TaskRow> column) =>
        await _tasks.Repository.ReorderTasksAsync([.. column.Select(r => r.Id)]);

    private async void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Guid id && id != Guid.Empty)
        {
            await Shell.Current.GoToAsync($"{nameof(TaskDetailPage)}?taskId={id}");
        }
    }

    // -----------------------------------------------------------------------
    // Filtros y etiquetas
    // -----------------------------------------------------------------------

    private void BuildFilters()
    {
        FilterBox.Clear();

        foreach (var filter in TaskFilters.All)
        {
            FilterBox.Add(BuildChip(filter));
        }
    }

    private View BuildChip(TaskFilter filter)
    {
        var button = new Button
        {
            Text = Localization.Loc.Instance[TaskFilters.KeyOf(filter)],
            FontSize = 13,
            Padding = new Thickness(14, 6),
            MinimumHeightRequest = 0,
            CornerRadius = 16,
            ClassId = filter.ToString(),
        };

        Paint(button, filter == _filter);

        button.Clicked += async (_, _) =>
        {
            _filter = filter;

            foreach (var chip in FilterBox.OfType<Button>())
            {
                Paint(chip, chip.ClassId == filter.ToString());
            }

            await ReloadAsync();
        };

        return button;
    }

    /// <summary>Las etiquetas con algo pendiente detras, igual que en «Mis tareas».</summary>
    private async Task RefreshTagFilterAsync()
    {
        var tags = await _tasks.Repository.GetTagsAsync(pendingOnly: true);

        TagFilterScroll.IsVisible = tags.Count > 0;
        TagFilterBox.Clear();

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

        TagFilterBox.Add(BuildTagChip(Localization.Loc.Instance["AllTags"], null));
        TagFilterBox.Add(BuildTagChip(Localization.Loc.Instance["NoTagFilter"], TaskRepository.NoTag));

        foreach (var tag in tags)
        {
            TagFilterBox.Add(BuildTagChip($"#{tag}", tag));
        }
    }

    private View BuildTagChip(string text, string? tag)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 13,
            Padding = new Thickness(14, 6),
            MinimumHeightRequest = 0,
            CornerRadius = 16,
        };

        Paint(button, string.Equals(_activeTag, tag, StringComparison.CurrentCultureIgnoreCase));

        button.Clicked += async (_, _) =>
        {
            _activeTag = tag;
            await ReloadAsync();
        };

        return button;
    }

    private static void Paint(Button chip, bool active)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;

        chip.BackgroundColor = active
            ? Color.FromArgb("#3525CD")
            : Color.FromArgb(dark ? "#2A2833" : "#EDEEEF");

        chip.TextColor = active ? Colors.White : Color.FromArgb(dark ? "#E6E1E9" : "#191C1D");
    }

    private async void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _search = e.NewTextValue;
        await ReloadAsync();
    }

    /// <summary>
    /// Crear desde el tablero: a la primera lista, en «por hacer», y se abre el detalle, igual que
    /// la captura rapida de «Mis tareas».
    /// </summary>
    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var title = QuickAdd.Text?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return;
        }

        var list = await _tasks.Repository
            .GetOrCreateDefaultListAsync(Localization.Loc.Instance["DefaultListName"]);

        var task = await _tasks.Repository.AddTaskAsync(list.Id, title);
        QuickAdd.Text = string.Empty;
        await ReloadAsync();

        await Shell.Current.GoToAsync($"{nameof(TaskDetailPage)}?taskId={task.Id}");
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await ServiceHelper.GetRequiredService<SyncCoordinator>().RefreshNowAsync();
        await ReloadAsync();
    }
}
