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
    /// Enseña la invitacion: el QR, y debajo el codigo y la clave por si hay que teclearlos.
    /// </summary>
    /// <remarks>
    /// <b>De momento solo el QR.</b> Antes se preguntaba primero si mandarla por correo, por
    /// WhatsApp o copiarla, y esa pregunta se ha quitado mientras se prueba el escaneo: el camino
    /// que hay que dejar limpio es enseñar el codigo y que el otro aparato lo lea. La invitacion se
    /// sigue dejando en el portapapeles al pasar por aqui, que no cuesta nada y no pregunta nada.
    /// </remarks>
    private async Task CompartirAsync(string groupName, GroupInvite invite)
    {
        var texto = GroupLink.Message(
            Helpers.ServiceHelper.GetRequiredService<LocalizationService>(), groupName, invite);

        await Clipboard.SetTextAsync(texto);
        await Navigation.PushAsync(new GroupQrPage(groupName, invite));
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

        await EntrarAsync(invite);
    }

    /// <summary>
    /// Lee el QR de una invitacion con la camara y entra en el grupo.
    /// </summary>
    /// <remarks>
    /// El QR ya se pintaba, pero no habia con que leerlo: los lectores de codigos del movil abren
    /// direcciones web y una invitacion es un enlace propio que solo entiende esta aplicacion, asi
    /// que enfocarlo con cualquier otro lector no hacia nada. Se pregunta igual que con un enlace
    /// recibido, que para el caso es lo mismo: algo de fuera que mete al usuario en un grupo.
    /// </remarks>
    private async void OnScanQrClicked(object? sender, EventArgs e)
    {
        GroupInvite? leido;
        try
        {
            leido = await ScanQrPage.PedirAsync(this);
        }
        catch (Exception ex)
        {
            // Se enseña el fallo en vez de no hacer nada: una pantalla que no reacciona al pulsarla
            // no se puede ni contar ni arreglar.
            Android.Util.Log.Error("TMQR", ex.ToString());
            await SocShared.ModernDialog.AlertAsync(
                this, Localization.Loc.Instance["ScanTitle"], ex.Message,
                Localization.Loc.Instance["Ok"]);
            return;
        }

        if (leido is not { } invite)
        {
            return;
        }

        var entrar = await SocShared.ModernDialog.AlertAsync(
            this,
            Localization.Loc.Instance["JoinFromLinkTitle"],
            Localization.Loc.Instance.Format("JoinFromLinkMessage", invite.JoinCode),
            Localization.Loc.Instance["Join"],
            Localization.Loc.Instance["Cancel"]);

        if (entrar)
        {
            await EntrarAsync(invite);
        }
    }

    /// <summary>
    /// Entrar en un grupo, venga la invitacion de donde venga: tecleada, de un enlace o de un QR.
    /// </summary>
    /// <remarks>
    /// Se recoge <b>cualquier</b> excepcion: una clave mal escrita llega como un 400, o sea una
    /// <c>HttpRequestException</c>, y con un catch estrecho no la recogia nadie y la aplicacion se
    /// cerraba en seco (visto en el Xiaomi el 2026-09-06).
    /// </remarks>
    private async Task EntrarAsync(GroupInvite invite)
    {
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
        var textos = Localization.Loc.Instance;

        var code = await SocShared.ModernDialog.PromptAsync(this, textos["JoinGroupTitle"],
            textos["JoinGroupMessage"], textos["Next"], textos["Cancel"], placeholder: "ABC123");

        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var key = await SocShared.ModernDialog.PromptAsync(this, textos["SharedKeyTitle"], null,
            textos["Join"], textos["Cancel"], placeholder: textos["SharedKeyPlaceholder"]);

        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        await EntrarAsync(new GroupInvite(code.Trim(), key.Trim()));
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
