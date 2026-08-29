using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;

namespace TaskManager.Mobile.Pages;

/// <summary>Grupo con sus listas, tal como se pinta en la pantalla.</summary>
public sealed class GroupRow
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Caption { get; init; }

    public required List<ListRow> Lists { get; init; }
}

/// <summary>
/// "Mis grupos" (especificacion 2 y 3): espacios compartidos, cada uno con varias listas. Entrar a
/// un grupo exige la clave compartida, que se comprueba en el servidor, nunca en el movil
/// (ARQUITECTURA.md seccion 4).
/// </summary>
public partial class GroupsPage : ContentPage
{
    private readonly TaskService _tasks;
    private readonly ISyncService _sync;

    public GroupsPage()
        : this(ServiceHelper.GetRequiredService<TaskService>(), ServiceHelper.GetRequiredService<ISyncService>())
    {
    }

    public GroupsPage(TaskService tasks, ISyncService sync)
    {
        InitializeComponent();
        _tasks = tasks;
        _sync = sync;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _tasks.InitializeAsync();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var rows = new List<GroupRow>();

        foreach (var group in await _tasks.Repository.GetGroupsAsync())
        {
            var lists = new List<ListRow>();
            foreach (var list in await _tasks.Repository.GetGroupListsAsync(group.Id))
            {
                var tasks = await _tasks.Repository.GetTasksAsync(list.Id);
                var pending = tasks.Count(t => !t.IsDone);
                lists.Add(new ListRow
                {
                    Id = list.Id,
                    Name = list.Name,
                    Caption = tasks.Count == 0
                        ? "Vacía"
                        : pending == 0 ? $"{tasks.Count} completadas" : $"{pending} de {tasks.Count} pendientes",
                });
            }

            rows.Add(new GroupRow
            {
                Id = group.Id,
                Name = group.Name,
                Caption = $"Código {group.JoinCode} · {lists.Count} listas",
                Lists = lists,
            });
        }

        GroupsView.ItemsSource = rows;
    }

    // -----------------------------------------------------------------------

    private async void OnNewGroupClicked(object? sender, EventArgs e)
    {
        var name = await SocShared.ModernDialog.PromptAsync(this, "Nuevo grupo", null, "Siguiente", "Cancelar",
            placeholder: "Familia, Piso compartido, Proyecto...");

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var key = await SocShared.ModernDialog.PromptAsync(this, "Clave compartida",
            "Quien tenga esta clave y el código del grupo podrá entrar. Mínimo 6 caracteres.",
            "Crear", "Cancelar", placeholder: "clave del grupo");

        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length < 6)
        {
            await SocShared.ModernDialog.AlertAsync(this, "Clave demasiado corta",
                "La clave compartida necesita al menos 6 caracteres.", "OK");
            return;
        }

        var code = await _sync.CreateGroupAsync(name.Trim(), key.Trim());
        var group = await _tasks.Repository.SaveGroupAsync(new TaskGroup { Name = name.Trim(), JoinCode = code });

        // Un grupo sin lista no sirve de nada: se crea la primera con el nombre del grupo.
        await _tasks.Repository.CreateListAsync("General", group.Id);
        await ReloadAsync();

        await SocShared.ModernDialog.AlertAsync(this, "Grupo creado",
            _sync.IsConfigured
                ? $"Código del grupo: {code}\n\nQuien quiera entrar necesita ese código y la clave compartida."
                : $"Código del grupo: {code}\n\nTodavía no hay servidor configurado, así que el grupo existe " +
                  "solo en este dispositivo. En cuanto se configure Supabase, la clave pasará a comprobarse allí.",
            "OK");
    }

    private async void OnJoinGroupClicked(object? sender, EventArgs e)
    {
        var code = await SocShared.ModernDialog.PromptAsync(this, "Unirse a un grupo",
            "Código del grupo (6 caracteres).", "Siguiente", "Cancelar", placeholder: "ABC123");

        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var key = await SocShared.ModernDialog.PromptAsync(this, "Clave compartida", null,
            "Entrar", "Cancelar", placeholder: "clave del grupo");

        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        try
        {
            var groupId = await _sync.JoinGroupAsync(code.Trim(), key.Trim());
            await _sync.PullAsync();
            await ReloadAsync();

            await SocShared.ModernDialog.AlertAsync(this, "Ya estás dentro",
                $"Te has unido al grupo {groupId}.", "OK");
        }
        catch (InvalidOperationException ex)
        {
            // Sin servidor no se puede validar la clave: decirlo claro en vez de fingir que entra.
            await SocShared.ModernDialog.AlertAsync(this, "Todavía no se puede", ex.Message, "OK");
        }
    }

    private async void OnNewListClicked(object? sender, EventArgs e)
    {
        if (sender is not ImageButton { CommandParameter: Guid groupId })
        {
            return;
        }

        var name = await SocShared.ModernDialog.PromptAsync(this, "Nueva lista del grupo", null, "Crear", "Cancelar",
            placeholder: "Compras, Mantenimiento, Vacaciones...");

        if (!string.IsNullOrWhiteSpace(name))
        {
            await _tasks.Repository.CreateListAsync(name, groupId);
            await ReloadAsync();
        }
    }

    private async void OnDeleteGroupClicked(object? sender, EventArgs e)
    {
        if (sender is not ImageButton { CommandParameter: Guid groupId })
        {
            return;
        }

        var group = await _tasks.Repository.GetGroupAsync(groupId);
        if (group is null)
        {
            return;
        }

        var confirmed = await SocShared.ModernDialog.AlertAsync(this, "Salir del grupo",
            $"Se quitará «{group.Name}» de este dispositivo, con sus listas.", "Salir", "Cancelar");

        if (confirmed)
        {
            await _tasks.Repository.DeleteGroupAsync(group);
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
}
