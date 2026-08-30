using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;

namespace TaskManager.Mobile.Pages;

/// <summary>Fila de la lista: un elemento de trabajo tal y como se ve antes de importarlo.</summary>
public sealed class WorkItemRow
{
    public required WorkItem Item { get; init; }

    public int Id => Item.Id;

    public string Title => Item.TaskTitle;

    /// <summary>Proyecto, tipo y estado en una linea: es lo que distingue dos elementos parecidos.</summary>
    public string Caption =>
        string.Join(" · ", new[] { Item.Project, Item.Type, Item.State }.Where(s => s.Length > 0));
}

/// <summary>
/// Elementos de trabajo de Azure DevOps asignados al usuario, para pasarlos a tareas.
/// </summary>
/// <remarks>
/// <para>Se importan <b>de uno en uno y a mano</b>. Volcar el tablero entero de golpe llenaria Mi
/// Dia de cosas que no se van a hacer hoy; lo util es escoger lo que uno se compromete a sacar.</para>
///
/// <para>Lo importado no se sincroniza de vuelta: completar la tarea aqui no cierra el elemento en
/// DevOps. Hacerlo seria tocar el tablero de un equipo desde una aplicacion personal, y eso no le
/// corresponde decidirlo a esta pantalla.</para>
/// </remarks>
public partial class DevOpsPage : ContentPage
{
    private readonly TaskService _tasks;
    private readonly AzureDevOpsService _devops;

    private readonly List<WorkItemRow> _rows = [];

    private string? _token;
    private bool _busy;

    public DevOpsPage()
        : this(ServiceHelper.GetRequiredService<TaskService>(),
               ServiceHelper.GetRequiredService<AzureDevOpsService>())
    {
    }

    public DevOpsPage(TaskService tasks, AzureDevOpsService devops)
    {
        InitializeComponent();
        _tasks = tasks;
        _devops = devops;
    }

    private static Localization.Loc L => Localization.Loc.Instance;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _tasks.InitializeAsync();

        EmptyTitle.Text = L["DevOpsEmptyTitle"];
        EmptyMessage.Text = L["DevOpsEmptyMessage"];

        if (!AzureDevOpsService.IsConfigured)
        {
            SignInButton.IsEnabled = false;
            RefreshButton.IsEnabled = false;
            StatusLabel.Text = L["DevOpsNotConfigured"];
            return;
        }

        // Si ya se entro antes, la sesion se recupera sola y se cargan los elementos sin que haya
        // que volver a pulsar nada.
        var session = await _devops.RestoreAsync();
        if (session is not null)
        {
            _token = session.AccessToken;
            ShowSignedIn();
            await LoadAsync();
        }
    }

    // -----------------------------------------------------------------------

    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        StatusLabel.Text = L["MailSigningIn"];

        try
        {
            var session = await _devops.SignInAsync();
            _token = session.AccessToken;
            ShowSignedIn();
            await LoadAsync();
        }
        catch (TaskCanceledException)
        {
            StatusLabel.Text = L["SignInCancelled"];
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await LoadAsync();

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadAsync();
        Refresher.IsRefreshing = false;
    }

    private async Task LoadAsync()
    {
        if (_token is null || _busy)
        {
            return;
        }

        SetBusy(true);
        StatusLabel.Text = L["DevOpsLoading"];

        try
        {
            var items = await _devops.GetAssignedAsync(_token);

            _rows.Clear();
            _rows.AddRange(items.Select(i => new WorkItemRow { Item = i }));
            ItemsView.ItemsSource = null;
            ItemsView.ItemsSource = _rows;

            StatusLabel.Text = _rows.Count == 0
                ? L["DevOpsNone"]
                : L.Format("DevOpsCount", _rows.Count);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnImportClicked(object? sender, EventArgs e)
    {
        if (sender is not ImageButton { CommandParameter: int id })
        {
            return;
        }

        var row = _rows.FirstOrDefault(r => r.Id == id);
        if (row is null)
        {
            return;
        }

        var lists = await _tasks.Repository.GetPrivateListsAsync();
        var listId = lists.FirstOrDefault()?.Id
            ?? (await _tasks.Repository.CreateListAsync(L["DefaultListName"])).Id;

        var task = await _tasks.Repository.AddTaskAsync(listId, row.Title, inMyDay: true);

        // El enlace va en el contexto y no en las notas: asi el desglose con IA lo tiene delante, y
        // ademas queda a un toque volver al elemento original.
        task.Context = L.Format("DevOpsContext", row.Item.Organization, row.Item.Project, row.Item.Url);
        task.Tags = TaskTags.FromInput("devops");
        task.DueAt = row.Item.DueDate;
        await _tasks.Repository.UpdateTaskAsync(task);

        StatusLabel.Text = L.Format("TaskCreated", task.Title);
    }

    // -----------------------------------------------------------------------

    private void ShowSignedIn()
    {
        SignInButton.IsVisible = false;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshButton.IsEnabled = !busy;
        SignInButton.IsEnabled = !busy;
    }
}
