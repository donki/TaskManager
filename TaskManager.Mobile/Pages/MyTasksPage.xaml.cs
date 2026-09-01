using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;
using TaskManager.Mobile.Models;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// «Mis tareas»: <b>todas</b> las tareas del usuario, de todas sus listas, con un filtro encima.
/// </summary>
/// <remarks>
/// <para>Es la pantalla de entrada. Antes lo era «Mi Día», que solo enseñaba lo marcado para hoy:
/// util para trabajar, pero deja fuera todo lo demas y obliga a ir lista por lista para saber que
/// hay. Aqui se ve todo y se acota con el filtro, que es la misma lista de criterios que en Windows
/// (<see cref="TaskFilters"/>).</para>
///
/// <para>Arranca en <see cref="TaskFilter.Pending"/> porque a lo que se viene es a lo que queda por
/// hacer; lo terminado se consulta, no se vigila.</para>
/// </remarks>
public partial class MyTasksPage : ContentPage
{
    private readonly TaskService _tasks;
    private readonly SettingsService _settings;
    private readonly Dictionary<Guid, string> _listNames = [];

    private TaskFilter _filter = TaskFilters.Default;
    private string? _activeTag;
    private Guid _defaultListId;

    public MyTasksPage()
        : this(ServiceHelper.GetRequiredService<TaskService>(), ServiceHelper.GetRequiredService<SettingsService>())
    {
    }

    public MyTasksPage(TaskService tasks, SettingsService settings)
    {
        InitializeComponent();

        _tasks = tasks;
        _settings = settings;

        BuildFilters();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _tasks.InitializeAsync();
        Celebration.HapticsEnabled = _settings.HapticsEnabled;
        await ReloadAsync();
        await AskForNotificationsOnceAsync();
    }

    /// <summary>
    /// Pide el permiso de notificaciones la primera vez que se llega aqui.
    /// </summary>
    /// <remarks>
    /// <para>Hacia falta porque los recordatorios vienen <b>encendidos por defecto</b> y el permiso
    /// solo se pedia al tocarlos en Ajustes: quien no entraba ahi nunca lo concedia. El resultado
    /// era que la aplicacion encolaba sus avisos y Android los tiraba sin decir nada
    /// (<c>importance=NONE</c>, <c>numPostedByApp=0</c>). Comprobado en el Xiaomi el 2026-09-01.</para>
    ///
    /// <para>Se pide aqui y no al arrancar: en la pantalla de entrada, un dialogo del sistema encima
    /// de otro es justo lo que hace que la gente pulse «no» sin leer.</para>
    /// </remarks>
    private async Task AskForNotificationsOnceAsync()
    {
        const string key = "notify.asked";

        if (_settings.GetBool(key, false) || !_settings.NotificationsEnabled)
        {
            return;
        }

        await _settings.SetBoolAsync(key, true);

        try
        {
            var notifications = ServiceHelper.GetRequiredService<INotificationService>();

            if (!await notifications.IsAllowedAsync())
            {
                await notifications.RequestPermissionAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Permiso de notificaciones: {ex.Message}");
        }
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await ReloadAsync();
        Refresher.IsRefreshing = false;
    }

    private async Task ReloadAsync()
    {
        await LoadListNamesAsync();
        await RefreshTagFilterAsync();

        var tasks = await _tasks.Repository.GetAllTasksAsync(_filter, _activeTag);

        TasksView.ItemsSource = tasks
            .Select(t => new TaskRow(t, _listNames.GetValueOrDefault(t.ListId, string.Empty)))
            .ToList();

        FilterLabel.Text = _activeTag is null
            ? Localization.Loc.Instance[TaskFilters.KeyOf(_filter)]
            : $"{Localization.Loc.Instance[TaskFilters.KeyOf(_filter)]}  ·  #{_activeTag}";
        SummaryLabel.Text = tasks.Count == 1
            ? Localization.Loc.Instance["TaskCountOne"]
            : Localization.Loc.Instance.Format("TaskCount", tasks.Count);
    }

    /// <summary>
    /// Los filtros se pintan una sola vez; cambiar de filtro solo repinta el color del activo, sin
    /// rehacer la fila entera.
    /// </summary>
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

    /// <summary>
    /// Pinta las etiquetas que hay en uso. Se rehace en cada recarga porque la ultima tarea que
    /// llevaba una etiqueta puede haberse borrado, y entonces esa etiqueta ya no existe.
    /// </summary>
    private async Task RefreshTagFilterAsync()
    {
        var tags = await _tasks.Repository.GetTagsAsync();

        TagFilterScroll.IsVisible = tags.Count > 0;
        TagFilterBox.Clear();

        if (tags.Count == 0)
        {
            _activeTag = null;
            return;
        }

        // La etiqueta activa pudo desaparecer al borrar la ultima tarea que la llevaba.
        if (_activeTag is not null && !tags.Contains(_activeTag, StringComparer.CurrentCultureIgnoreCase))
        {
            _activeTag = null;
        }

        TagFilterBox.Add(BuildTagChip(Localization.Loc.Instance["AllTags"], null));
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

        chip.TextColor = active
            ? Colors.White
            : Color.FromArgb(dark ? "#E6E1E9" : "#191C1D");
    }

    /// <summary>
    /// El nombre de la lista va en la fila porque aqui se mezclan tareas de todas las listas y si
    /// no, no se sabe de donde sale cada una.
    /// </summary>
    private async Task LoadListNamesAsync()
    {
        _listNames.Clear();

        var lists = await _tasks.Repository.GetPrivateListsAsync();
        foreach (var list in lists)
        {
            _listNames[list.Id] = list.Name;
        }

        _defaultListId = lists.FirstOrDefault()?.Id ?? Guid.Empty;
    }

    private async void OnTaskTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Guid id)
        {
            await Shell.Current.GoToAsync($"{nameof(TaskDetailPage)}?taskId={id}");
        }
    }

    // -----------------------------------------------------------------------

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var title = QuickAdd.Text?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return;
        }

        if (_defaultListId == Guid.Empty)
        {
            _defaultListId = (await _tasks.Repository
                .GetOrCreateDefaultListAsync(Localization.Loc.Instance["DefaultListName"])).Id;
        }

        await _tasks.Repository.AddTaskAsync(_defaultListId, title);
        QuickAdd.Text = string.Empty;
        await ReloadAsync();
    }

    private async void OnToggleDoneClicked(object? sender, EventArgs e)
    {
        if (sender is not ImageButton { CommandParameter: Guid id })
        {
            return;
        }

        var task = await _tasks.Repository.GetTaskAsync(id);
        if (task is null)
        {
            return;
        }

        if (task.IsDone)
        {
            await _tasks.UncompleteTaskAsync(task);
            await ReloadAsync();
            return;
        }

        var celebration = await _tasks.CompleteTaskAsync(task);
        await ReloadAsync();

        if (celebration is not null)
        {
            Celebration.Celebrate(celebration);
        }
    }
}
