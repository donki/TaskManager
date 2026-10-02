using TaskManager.Core.Services;

namespace TaskManager.Desktop.Tests.Banco;

/// <summary>El servidor de grupos, de mentira: contesta lo que diga la prueba y apunta lo que se le pide.</summary>
internal sealed class SyncFalso : ISyncService
{
    public bool IsConfigured { get; set; } = true;

    public event EventHandler<RemoteChange>? RemoteChanged;

    public List<string> Llamadas { get; } = [];

    /// <summary>Si no es nulo, las llamadas de grupos fallan con esto (sin red, clave mala...).</summary>
    public Exception? Fallo { get; set; }

    public bool? Propietario { get; set; } = true;

    public Guid GrupoAlEntrar { get; set; } = Guid.NewGuid();

    public void Avisar(RemoteChange cambio) => RemoteChanged?.Invoke(this, cambio);

    private Task<T> Responder<T>(string que, T valor)
    {
        Llamadas.Add(que);
        return Fallo is { } f ? Task.FromException<T>(f) : Task.FromResult(valor);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Llamadas.Add("start");
        return Task.CompletedTask;
    }

    public Task<int> PushAsync(CancellationToken cancellationToken = default)
    {
        Llamadas.Add("push");
        return Task.FromResult(0);
    }

    public Task PullAsync(CancellationToken cancellationToken = default)
    {
        Llamadas.Add("pull");
        return Task.CompletedTask;
    }

    public Task<GroupInvite> CreateGroupAsync(Guid id, string name, CancellationToken cancellationToken = default) =>
        Responder($"crear:{name}", new GroupInvite("ABC123", "clave-nueva"));

    public Task<Guid> JoinGroupAsync(string joinCode, string sharedKey, CancellationToken cancellationToken = default) =>
        Responder($"entrar:{joinCode}:{sharedKey}", GrupoAlEntrar);

    public Task<GroupInvite> RenewInviteAsync(Guid groupId, string joinCode, CancellationToken cancellationToken = default) =>
        Responder($"renovar:{joinCode}", new GroupInvite(joinCode, "clave-renovada"));

    public Task<bool?> IsGroupOwnerAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        Responder("propietario", Propietario);

    public Task LeaveGroupAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        Responder("salir", true);

    public Task DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        Responder("borrar", true);
}
