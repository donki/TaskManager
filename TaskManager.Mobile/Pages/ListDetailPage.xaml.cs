using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;
using TaskManager.Mobile.Models;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// Tareas de una lista, sea privada o de un grupo: la misma pantalla para las dos, porque una lista
/// de grupo no es otra cosa (ARQUITECTURA.md seccion 2).
/// </summary>
[QueryProperty(nameof(ListId), "listId")]
public partial class ListDetailPage : ContentPage
{
    private readonly TaskService _tasks;
    private readonly SettingsService _settings;

    private Guid _listId;

    public ListDetailPage()
        : this(ServiceHelper.GetRequiredService<TaskService>(), ServiceHelper.GetRequiredService<SettingsService>())
    {
    }

    public ListDetailPage(TaskService tasks, SettingsService settings)
    {
        InitializeComponent();
        _tasks = tasks;
        _settings = settings;
    }

    /// <summary>Llega por la ruta de navegacion: <c>ListDetailPage?listId=...</c>.</summary>
    public string ListId
    {
        set => _listId = Guid.TryParse(Uri.UnescapeDataString(value), out var id) ? id : Guid.Empty;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _tasks.InitializeAsync();
        Celebration.HapticsEnabled = _settings.HapticsEnabled;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (_listId == Guid.Empty)
        {
            return;
        }

        var list = await _tasks.Repository.GetListAsync(_listId);
        Title = list?.Name ?? "Lista";

        var tasks = await _tasks.Repository.GetTasksAsync(_listId);
        var rows = new List<TaskRow>();

        foreach (var task in tasks)
        {
            var steps = await _tasks.Repository.GetStepsAsync(task.Id);
            rows.Add(new TaskRow(task) { Steps = steps.Select(s => new StepRow(s)).ToList() });
        }

        TasksView.ItemsSource = rows;
    }

    // -----------------------------------------------------------------------

    private async void OnAddClicked(object? sender, EventArgs e) => await AddTaskAsync();

    private async Task<TaskItem?> AddTaskAsync()
    {
        var title = QuickAdd.Text?.Trim();
        if (string.IsNullOrEmpty(title) || _listId == Guid.Empty)
        {
            return null;
        }

        var task = await _tasks.Repository.AddTaskAsync(_listId, title);
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
        await ReloadAsync();

        if (celebration is not null)
        {
            Celebration.Celebrate(celebration);
        }
    }

    private async void OnToggleMyDayClicked(object? sender, EventArgs e)
    {
        if (sender is not ImageButton { CommandParameter: Guid id })
        {
            return;
        }

        var task = await _tasks.Repository.GetTaskAsync(id);
        if (task is not null)
        {
            await _tasks.Repository.ToggleMyDayAsync(task);
            await ReloadAsync();
        }
    }

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
