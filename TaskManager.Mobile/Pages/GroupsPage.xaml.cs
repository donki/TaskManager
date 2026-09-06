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
        await EntrarSiHayInvitacionAsync();
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
        var name = await SocShared.ModernDialog.PromptAsync(this, "Nuevo grupo", null, "Crear", "Cancelar",
            placeholder: "Familia, Piso compartido, Proyecto...");

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // El grupo se guarda aqui primero porque su identificador es la clave con la que se cifra
        // su nombre antes de mandarlo.
        var group = await _tasks.Repository.SaveGroupAsync(new TaskGroup { Name = name.Trim() });

        try
        {
            // La clave compartida ya no se pide: la genera la aplicacion (ver GroupInvite) y sale
            // una sola vez, aqui.
            var invite = await _sync.CreateGroupAsync(group.Id, name.Trim());

            group.JoinCode = invite.JoinCode;
            await _tasks.Repository.SaveGroupAsync(group);

            // Un grupo sin lista no sirve de nada: se crea la primera con el nombre del grupo.
            await _tasks.Repository.CreateListAsync("General", group.Id);
            await ReloadAsync();

            await CompartirAsync(group.Name, invite);
        }
        catch (Exception ex)
        {
            // Sin esto, un fallo del servidor no era un aviso: era un cierre de la aplicacion —el
            // metodo es `async void` y la excepcion no la recogia nadie— y encima dejaba el grupo a
            // medias, sin codigo, ocupando sitio en la pantalla.
            await _tasks.Repository.DeleteGroupAsync(group);
            await ReloadAsync();

            await SocShared.ModernDialog.AlertAsync(
                this, Localization.Loc.Instance["NotYetTitle"], ex.Message,
                Localization.Loc.Instance["Ok"]);
        }
    }

    /// <summary>
    /// Reparte una invitacion nueva para un grupo que ya existe.
    /// </summary>
    /// <remarks>
    /// La clave no se guarda en ninguna parte, asi que la que salio al crear el grupo no se puede
    /// volver a mirar: para invitar mas tarde se pone una nueva, y la anterior deja de valer. Es lo
    /// sano —una invitacion olvidada en un chat ya no abre nada— y a quien ya esta dentro no le
    /// afecta, porque su pertenencia ya esta canjeada.
    /// </remarks>
    private async void OnInviteClicked(object? sender, EventArgs e)
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

        var seguir = await SocShared.ModernDialog.AlertAsync(
            this,
            Localization.Loc.Instance["InviteTitle"],
            Localization.Loc.Instance["InviteWarning"],
            Localization.Loc.Instance["InviteCreate"],
            Localization.Loc.Instance["Cancel"]);

        if (!seguir)
        {
            return;
        }

        try
        {
            var invite = await _sync.RenewInviteAsync(group.Id, group.JoinCode);
            await CompartirAsync(group.Name, invite);
        }
        catch (Exception ex)
        {
            await SocShared.ModernDialog.AlertAsync(
                this, Localization.Loc.Instance["NotYetTitle"], ex.Message,
                Localization.Loc.Instance["Ok"]);
        }
    }

    /// <summary>
    /// Enseña la invitacion, la deja en el portapapeles y ofrece mandarla por donde sea.
    /// </summary>
    /// <remarks>
    /// La hoja de compartir del sistema trae WhatsApp, el correo, Telegram y lo que tenga puesto el
    /// usuario, que es mejor que elegir nosotros tres canales.
    /// </remarks>
    private async Task CompartirAsync(string groupName, GroupInvite invite)
    {
        var texto = GroupLink.Message(
            Helpers.ServiceHelper.GetRequiredService<LocalizationService>(), groupName, invite);

        await Clipboard.SetTextAsync(texto);

        var aviso = texto + Environment.NewLine + Environment.NewLine +
                    Localization.Loc.Instance["GroupInviteSaved"];

        if (!_sync.IsConfigured)
        {
            aviso += Environment.NewLine + Environment.NewLine +
                     "Todavía no hay servidor configurado: el grupo existe solo en este dispositivo.";
        }

        // Se lleva a la pantalla del QR, que es la que de verdad sirve para invitar a alguien que
        // esta al lado: enfoca con su camara y entra. Desde ahi se puede mandar o copiar.
        var verQr = await SocShared.ModernDialog.AlertAsync(
            this,
            Localization.Loc.Instance["GroupCreated"],
            aviso,
            Localization.Loc.Instance["QrTitle"],
            Localization.Loc.Instance["Ok"]);

        if (verQr)
        {
            await Navigation.PushAsync(new GroupQrPage(groupName, invite));
        }
    }

    /// <summary>
    /// Entra en el grupo de una invitacion que ha llegado de fuera: un QR escaneado o un enlace
    /// tocado.
    /// </summary>
    /// <remarks>
    /// Se pregunta antes: el enlace puede haber llegado por cualquier sitio, y meter a alguien en un
    /// grupo sin decirle nada seria pasarse. La invitacion se consume aunque diga que no, para que
    /// no vuelva a saltar en cada visita a esta pantalla.
    /// </remarks>
    private async Task EntrarSiHayInvitacionAsync()
    {
        if (Services.GroupInviteLinks.Recoger() is not { } invite)
        {
            return;
        }

        var entrar = await SocShared.ModernDialog.AlertAsync(
            this,
            Localization.Loc.Instance["JoinFromLinkTitle"],
            Localization.Loc.Instance.Format("JoinFromLinkMessage", invite.JoinCode),
            Localization.Loc.Instance["Join"],
            Localization.Loc.Instance["Cancel"]);

        if (!entrar)
        {
            return;
        }

        try
        {
            await _sync.JoinGroupAsync(invite.JoinCode, invite.SharedKey);
            await _sync.PullAsync();
            await ReloadAsync();

            await SocShared.ModernDialog.AlertAsync(
                this, Localization.Loc.Instance["JoinedTitle"],
                Localization.Loc.Instance["GroupCodeShare"], Localization.Loc.Instance["Ok"]);
        }
        catch (Exception ex)
        {
            await SocShared.ModernDialog.AlertAsync(
                this, Localization.Loc.Instance["NotYetTitle"], ex.Message,
                Localization.Loc.Instance["Ok"]);
        }
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
        catch (Exception ex)
        {
            // TODAS, no solo InvalidOperationException: una clave mal escrita llega como un 400 y
            // eso es una HttpRequestException, que con el catch estrecho no la recogia nadie y
            // cerraba la aplicacion en seco (visto en el Xiaomi el 2026-09-06).
            await SocShared.ModernDialog.AlertAsync(
                this, Localization.Loc.Instance["NotYetTitle"], ex.Message,
                Localization.Loc.Instance["Ok"]);
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
