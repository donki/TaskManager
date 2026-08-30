using System.Globalization;
using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;
using TaskManager.Mobile.Models;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// "Mi Dia" (especificacion 3): solo lo elegido para hoy, personal o de grupo. No hay lista fisica
/// detras; son las tareas cuya fecha de Mi Dia es hoy, de modo que a medianoche la vista se vacia
/// sola sin perder nada.
/// </summary>
public partial class MyDayPage : ContentPage
{
    private readonly TaskService _tasks;
    private readonly SettingsService _settings;
    private readonly Dictionary<Guid, string> _listNames = [];

    private Guid _defaultListId;
    private string? _activeTag;

    public MyDayPage()
        : this(ServiceHelper.GetRequiredService<TaskService>(), ServiceHelper.GetRequiredService<SettingsService>())
    {
    }

    public MyDayPage(TaskService tasks, SettingsService settings)
    {
        InitializeComponent();

        _tasks = tasks;
        _settings = settings;

        var culture = new CultureInfo("es-ES");
        DateLabel.Text = DateTime.Now.ToString("dddd, d 'de' MMMM", culture);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _tasks.InitializeAsync();
        Celebration.HapticsEnabled = _settings.HapticsEnabled;
        await ReloadAsync();
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

        var tasks = await _tasks.Repository.GetMyDayAsync(tag: _activeTag);
        TasksView.ItemsSource = tasks
            .Select(t => new TaskRow(t, _listNames.GetValueOrDefault(t.ListId, string.Empty)))
            .ToList();

        var pending = tasks.Count(t => !t.IsDone);
        SummaryLabel.Text = tasks.Count switch
        {
            0 => Localization.Loc.Instance["NothingToday"],
            _ when pending == 0 => Localization.Loc.Instance["AllDone"],
            1 => Localization.Loc.Instance["OnePending"],
            _ => Localization.Loc.Instance.Format("ManyPending", pending),
        };
    }

    /// <summary>
    /// El nombre de la lista se muestra en la fila porque en Mi Dia se mezclan tareas privadas y de
    /// varios grupos, y si no no se sabe de donde sale cada una.
    /// </summary>
    private async Task LoadListNamesAsync()
    {
        _listNames.Clear();

        var privateLists = await _tasks.Repository.GetPrivateListsAsync();
        foreach (var list in privateLists)
        {
            _listNames[list.Id] = list.Name;
        }

        _defaultListId = privateLists.FirstOrDefault()?.Id ?? Guid.Empty;

        foreach (var group in await _tasks.Repository.GetGroupsAsync())
        {
            foreach (var list in await _tasks.Repository.GetGroupListsAsync(group.Id))
            {
                _listNames[list.Id] = $"{group.Name} · {list.Name}";
            }
        }
    }

    /// <summary>
    /// Pinta las etiquetas en uso como filtros. Si no hay ninguna, la fila no aparece: un filtro
    /// vacio solo ocupa sitio.
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

        TagFilterBox.Add(BuildTagChip(Localization.Loc.Instance["FilterAll"], null));
        foreach (var tag in tags)
        {
            TagFilterBox.Add(BuildTagChip(tag, tag));
        }
    }

    private View BuildTagChip(string text, string? tag)
    {
        var active = string.Equals(_activeTag, tag, StringComparison.CurrentCultureIgnoreCase);

        var button = new Button
        {
            Text = text,
            FontSize = 13,
            Padding = new Thickness(14, 6),
            MinimumHeightRequest = 0,
            CornerRadius = 16,
            BackgroundColor = active
                ? Color.FromArgb("#3525CD")
                : (Application.Current?.RequestedTheme == AppTheme.Dark
                    ? Color.FromArgb("#2A2833")
                    : Color.FromArgb("#EDEEEF")),
            TextColor = active
                ? Colors.White
                : (Application.Current?.RequestedTheme == AppTheme.Dark
                    ? Color.FromArgb("#E6E1E9")
                    : Color.FromArgb("#191C1D")),
        };

        button.Clicked += async (_, _) =>
        {
            _activeTag = tag;
            await ReloadAsync();
        };

        return button;
    }

    private async void OnTaskTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Guid id)
        {
            await Shell.Current.GoToAsync($"{nameof(TaskDetailPage)}?taskId={id}");
        }
    }

    // -----------------------------------------------------------------------
    // Acciones
    // -----------------------------------------------------------------------

    private async void OnAddClicked(object? sender, EventArgs e) => await AddTaskAsync();

    private async Task<TaskItem?> AddTaskAsync()
    {
        var title = QuickAdd.Text?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        if (_defaultListId == Guid.Empty)
        {
            var created = await _tasks.Repository.CreateListAsync("Tareas");
            _defaultListId = created.Id;
        }

        var task = await _tasks.Repository.AddTaskAsync(_defaultListId, title, inMyDay: true);
        QuickAdd.Text = string.Empty;
        await ReloadAsync();
        return task;
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

    /// <summary>
    /// La varita de la barra desglosa lo que hay escrito: se crea la tarea y se parte en pasos de
    /// un solo gesto, que es como se usa de verdad ("Organizar la mudanza" + varita).
    /// </summary>
    private async void OnBreakdownClicked(object? sender, EventArgs e)
    {
        var task = await AddTaskAsync();
        if (task is null)
        {
            await SocShared.ModernDialog.AlertAsync(this, Localization.Loc.Instance["MagicSteps"],
                Localization.Loc.Instance["MagicNeedGoal"], "OK");
            return;
        }

        await BreakdownAsync(task);
    }

    private async void OnRowBreakdownClicked(object? sender, EventArgs e)
    {
        if (sender is not ImageButton { CommandParameter: Guid id })
        {
            return;
        }

        var task = await _tasks.Repository.GetTaskAsync(id);
        if (task is not null)
        {
            await BreakdownAsync(task);
        }
    }

    private async Task BreakdownAsync(TaskItem task)
    {
        WandButton.IsEnabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var proposal = await _tasks.ProposeBreakdownAsync(task, cts.Token);

            if (!proposal.HasSomethingNew)
            {
                // Distinguir "no hay nada" de "ya los tienes todos": son dos situaciones distintas
                // y el usuario merece saber cual es.
                await SocShared.ModernDialog.AlertAsync(this, Localization.Loc.Instance["MagicSteps"],
                    proposal.AlreadyPresent > 0 ? Localization.Loc.Instance["MagicAllPresent"] : Localization.Loc.Instance["MagicNothing"],
                    "OK");
                return;
            }

            var detail = "• " + string.Join("\n• ", proposal.Steps);
            if (proposal.AlreadyPresent > 0)
                detail += "\n\n" + Localization.Loc.Instance.Format("MagicDiscarded", proposal.AlreadyPresent);

            var accepted = await SocShared.ModernDialog.AlertAsync(this,
                $"{Localization.Loc.Instance["MagicSteps"]} · {proposal.Source}", detail, Localization.Loc.Instance["MagicAdd"], Localization.Loc.Instance["MagicNotNow"]);

            if (!accepted)
                return;

            var (steps, celebration) = await _tasks.ApplyBreakdownAsync(task, proposal.Steps);
            await ReloadAsync();

            if (celebration is not null)
                Celebration.Celebrate(celebration);
        }
        finally
        {
            WandButton.IsEnabled = true;
        }
    }
}
