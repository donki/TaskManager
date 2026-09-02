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

    /// <summary>Crea el grupo en el servidor y devuelve su codigo de union.</summary>
    Task<string> CreateGroupAsync(Guid id, string name, string sharedKey, CancellationToken cancellationToken = default);

    /// <summary>Canjea codigo + clave compartida por pertenencia. Devuelve el id del grupo.</summary>
    Task<Guid> JoinGroupAsync(string joinCode, string sharedKey, CancellationToken cancellationToken = default);
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
    public Task<string> CreateGroupAsync(Guid id, string name, string sharedKey, CancellationToken cancellationToken = default)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var code = new string(Enumerable.Range(0, 6).Select(_ => alphabet[Random.Shared.Next(alphabet.Length)]).ToArray());
        return Task.FromResult(code);
    }

    public Task<Guid> JoinGroupAsync(string joinCode, string sharedKey, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Unirse a un grupo existente necesita Supabase configurado: la clave compartida se " +
            "comprueba en el servidor (join_group), nunca en el dispositivo.");

    private void OnRemoteChanged(RemoteChange change) => RemoteChanged?.Invoke(this, change);
}
