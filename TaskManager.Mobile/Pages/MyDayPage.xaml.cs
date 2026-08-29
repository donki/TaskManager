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

        var tasks = await _tasks.Repository.GetMyDayAsync();
        TasksView.ItemsSource = tasks
            .Select(t => new TaskRow(t, _listNames.GetValueOrDefault(t.ListId, string.Empty)))
            .ToList();

        var pending = tasks.Count(t => !t.IsDone);
        SummaryLabel.Text = tasks.Count switch
        {
            0 => "Nada por hoy",
            _ when pending == 0 => "Todo hecho por hoy",
            1 => "1 tarea pendiente",
            _ => $"{pending} tareas pendientes",
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
            await SocShared.ModernDialog.AlertAsync(this, "Pasos Mágicos",
                "Escribe primero el objetivo que quieres desglosar.", "OK");
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
                await SocShared.ModernDialog.AlertAsync(this, "Pasos Mágicos",
                    proposal.AlreadyPresent > 0
                        ? "Los pasos propuestos ya están en la tarea."
                        : "No ha salido ningún paso esta vez.",
                    "OK");
                return;
            }

            var detail = "• " + string.Join("\n• ", proposal.Steps);
            if (proposal.AlreadyPresent > 0)
                detail += $"\n\n({proposal.AlreadyPresent} ya estaban y se han descartado)";

            var accepted = await SocShared.ModernDialog.AlertAsync(this,
                $"Pasos Mágicos · {proposal.Source}", detail, "Añadir", "Ahora no");

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
