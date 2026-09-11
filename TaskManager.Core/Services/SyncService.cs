using TaskManager.Core.Data;

namespace TaskManager.Core.Services;

/// <param name="IsNew">
/// Si la fila <b>no existia aqui</b>. Es lo que separa «ha llegado una tarea» de «alguien ha
/// cambiado una tarea que ya tenias»: lo primero merece un aviso y lo segundo no, y sin este dato
/// no habia forma de distinguirlo — se avisaba de las dos y editar una tarea en el movil sacaba un
/// globo en Windows como si fuera nueva.
/// </param>
public sealed record RemoteChange(string Entity, string EntityId, bool IsNew = false);

/// <summary>
/// Puente con Supabase. La interfaz nunca depende de que exista: si no hay backend configurado se
/// usa <see cref="LocalOnlySyncService"/> y la aplicacion funciona entera contra SQLite.
/// </summary>
public interface ISyncService
{
    bool IsConfigured { get; }

    /// <summary>Cambios llegados de otros dispositivos (Realtime). Es lo que dispara la celebracion grupal.</summary>
    event EventHandler<RemoteChange>? RemoteChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Vacia la cola de salida contra el servidor. Devuelve cuantas operaciones subieron.</summary>
    Task<int> PushAsync(CancellationToken cancellationToken = default);

    Task PullAsync(CancellationToken cancellationToken = default);

    /// <summary>Crea el grupo en el servidor y devuelve lo que hace falta para entrar en el.</summary>
    Task<GroupInvite> CreateGroupAsync(Guid id, string name, CancellationToken cancellationToken = default);

    /// <summary>Canjea codigo + clave compartida por pertenencia. Devuelve el id del grupo.</summary>
    Task<Guid> JoinGroupAsync(string joinCode, string sharedKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Da una invitacion nueva para un grupo que ya existe: <b>clave nueva</b>, mismo codigo.
    /// </summary>
    /// <remarks>
    /// <para>La clave no se guarda en ningun aparato, asi que la que se enseño al crear el grupo no
    /// se puede volver a mirar. Para repartir una invitacion mas tarde no queda otra que poner una
    /// clave nueva —y es lo sano: la anterior deja de valer, asi que una invitacion que se quedo
    /// por ahi en un chat ya no abre nada.</para>
    ///
    /// <para>Solo puede hacerlo quien creo el grupo (<c>rotate_group_key</c> lo comprueba en el
    /// servidor). Los que ya estan dentro no se enteran: la pertenencia ya esta canjeada.</para>
    /// </remarks>
    Task<GroupInvite> RenewInviteAsync(Guid groupId, string joinCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Si quien esta dentro es quien creo el grupo, que es el unico que puede borrarlo.
    /// <c>null</c> cuando no se ha podido preguntar (sin red).
    /// </summary>
    Task<bool?> IsGroupOwnerAsync(Guid groupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Salir del grupo: se quita la pertenencia en el servidor. El grupo, sus listas y sus
    /// tareas siguen ahi para los demas miembros.
    /// </summary>
    Task LeaveGroupAsync(Guid groupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Borrar el grupo en el servidor, con sus listas y sus tareas, para todos los miembros. Solo
    /// puede hacerlo quien lo creo; a los demas el servidor no les deja (RLS).
    /// </summary>
    Task DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lo que hace falta para entrar en un grupo recien creado: el <b>codigo</b>, que es corto y se
/// puede dictar, y la <b>clave compartida</b>.
/// </summary>
/// <remarks>
/// <para><b>La clave la genera la aplicacion, no el usuario.</b> Antes se pedia escribirla, con un
/// minimo de seis caracteres, y eso es exactamente el sitio donde acaba habiendo un «familia2024»:
/// la clave es lo unico que separa a un grupo de cualquiera que adivine su codigo —de seis
/// caracteres— y el servidor la comprueba sin poder saber si es buena o mala. Un GUID no se
/// adivina, no se reutiliza de otro sitio y no hay que pensarlo.</para>
///
/// <para>No se guarda en el aparato: se enseña una vez al crear el grupo, para copiarla y pasarla a
/// quien tenga que entrar, y a partir de ahi vive solo en el servidor (hasheada) y en quien la haya
/// guardado.</para>
/// </remarks>
public sealed record GroupInvite(string JoinCode, string SharedKey)
{
    /// <summary>Una clave nueva. Un GUID en su forma de siempre, para poder copiarla entera.</summary>
    public static string NewKey() => Guid.NewGuid().ToString();
}

/// <summary>
/// Modo sin backend: la cola de salida se queda esperando y no se pierde nada. Es el modo en el que
/// esta el esqueleto hasta que exista el proyecto de Supabase (fase 4).
/// </summary>
public sealed class LocalOnlySyncService : ISyncService
{
    private readonly TaskRepository _repository;

    public LocalOnlySyncService(TaskRepository repository) => _repository = repository;

    public bool IsConfigured => false;

    public event EventHandler<RemoteChange>? RemoteChanged;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<int> PushAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public Task PullAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// En local el grupo existe igual (para poder montar listas y probar la interfaz), pero el
    /// codigo lo genera el dispositivo y la clave no protege nada hasta que haya servidor.
    /// </summary>
    public Task<GroupInvite> CreateGroupAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var code = new string(Enumerable.Range(0, 6).Select(_ => alphabet[Random.Shared.Next(alphabet.Length)]).ToArray());
        return Task.FromResult(new GroupInvite(code, GroupInvite.NewKey()));
    }

    public Task<GroupInvite> RenewInviteAsync(Guid groupId, string joinCode, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Repartir una invitacion nueva necesita Supabase configurado: la clave la guarda el " +
            "servidor (rotate_group_key), nunca el dispositivo.");

    public Task<Guid> JoinGroupAsync(string joinCode, string sharedKey, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Unirse a un grupo existente necesita Supabase configurado: la clave compartida se " +
            "comprueba en el servidor (join_group), nunca en el dispositivo.");

    // Sin servidor el grupo es solo de este aparato: se es dueño de todos y salir o borrar es lo
    // mismo, quitarlo de aqui.
    public Task<bool?> IsGroupOwnerAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        Task.FromResult<bool?>(true);

    public Task LeaveGroupAsync(Guid groupId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    private void OnRemoteChanged(RemoteChange change) => RemoteChanged?.Invoke(this, change);
}
