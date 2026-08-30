using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;

namespace TaskManager.Mobile.Pages;

/// <summary>Fila de lista, con el recuento de lo que queda por hacer.</summary>
public sealed class ListRow
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Caption { get; init; }
}

/// <summary>
/// "Mis listas privadas" (especificacion 3): las listas personales del usuario, las que no
/// pertenecen a ningun grupo.
/// </summary>
public partial class ListsPage : ContentPage
{
    private readonly TaskService _tasks;

    public ListsPage()
        : this(ServiceHelper.GetRequiredService<TaskService>())
    {
    }

    public ListsPage(TaskService tasks)
    {
        InitializeComponent();
        _tasks = tasks;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _tasks.InitializeAsync();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var lists = await _tasks.Repository.GetPrivateListsAsync();
        var rows = new List<ListRow>();

        foreach (var list in lists)
        {
            var tasks = await _tasks.Repository.GetTasksAsync(list.Id);
            var pending = tasks.Count(t => !t.IsDone);
            rows.Add(new ListRow
            {
                Id = list.Id,
                Name = list.Name,
                Caption = tasks.Count == 0
                    ? Localization.Loc.Instance["ListEmpty"]
                    : pending == 0
                        ? Localization.Loc.Instance.Format("ListAllDone", tasks.Count)
                        : Localization.Loc.Instance.Format("ListPending", pending, tasks.Count),
            });
        }

        ListsView.ItemsSource = rows;
    }

    private async void OnNewListClicked(object? sender, EventArgs e)
    {
        var name = await SocShared.ModernDialog.PromptAsync(this,
            Localization.Loc.Instance["NewListTitle"], null, Localization.Loc.Instance["Create"], Localization.Loc.Instance["Cancel"],
            placeholder: Localization.Loc.Instance["ListNamePlaceholder"]);

        if (!string.IsNullOrWhiteSpace(name))
        {
            await _tasks.Repository.CreateListAsync(name);
            await ReloadAsync();
        }
    }

    private async void OnListTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Guid id)
        {
            await Shell.Current.GoToAsync($"{nameof(ListDetailPage)}?listId={id}");
        }
    }

    private async void OnDeleteListClicked(object? sender, EventArgs e)
    {
        if (sender is not ImageButton { CommandParameter: Guid id })
        {
            return;
        }

        var list = await _tasks.Repository.GetListAsync(id);
        if (list is null)
        {
            return;
        }

        // Borrar una lista se lleva sus tareas por delante: se pregunta siempre.
        var confirmed = await SocShared.ModernDialog.AlertAsync(this,
            Localization.Loc.Instance["DeleteListTitle"], Localization.Loc.Instance.Format("DeleteListMessage", list.Name),
            Localization.Loc.Instance["Delete"], Localization.Loc.Instance["Cancel"]);

        if (confirmed)
        {
            await _tasks.Repository.DeleteListAsync(list);
            await ReloadAsync();
        }
    }
}
