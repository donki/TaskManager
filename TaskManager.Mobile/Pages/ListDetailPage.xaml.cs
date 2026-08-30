using System.Collections.ObjectModel;
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
    private ObservableCollection<TaskRow> _rows = [];

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

        // Coleccion observable, no una lista: al arrastrar, CollectionView mueve el elemento
        // dentro de la propia fuente, y con una List<> corriente el cambio no se ve.
        _rows = new ObservableCollection<TaskRow>(rows);
        TasksView.ItemsSource = _rows;
    }

    /// <summary>
    /// Guarda el orden manual despues de arrastrar.
    /// </summary>
    /// <remarks>
    /// No se recarga la lista al terminar: CollectionView ya ha dejado las filas donde el usuario
    /// las ha soltado, y volver a pintarlas provoca un parpadeo justo cuando acaba de levantar el
    /// dedo. Lo unico que hace falta es persistir el orden que ya se ve.
    /// </remarks>
    private async void OnReorderCompleted(object? sender, EventArgs e)
    {
        await _tasks.Repository.ReorderTasksAsync([.. _rows.Select(r => r.Id)]);
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

    /// <summary>Renombra la lista (nota de autor del 2026-08-29).</summary>
    private async void OnRenameListClicked(object? sender, EventArgs e)
    {
        var list = await _tasks.Repository.GetListAsync(_listId);
        if (list is null)
        {
            return;
        }

        var name = await SocShared.ModernDialog.PromptAsync(this,
            Localization.Loc.Instance["ListNameTitle"], null, Localization.Loc.Instance["Save"], Localization.Loc.Instance["Cancel"], initialValue: list.Name);

        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name == list.Name)
        {
            return;
        }

        list.Name = name;
        await _tasks.Repository.UpdateListAsync(list);
        Title = name;
    }

    private async void OnTaskTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Guid id)
        {
            await Shell.Current.GoToAsync($"{nameof(TaskDetailPage)}?taskId={id}");
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
