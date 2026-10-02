using TaskManager.Core.Services;

namespace TaskManager.Mobile.Tests.Infra;

/// <summary>El servidor de grupos de mentira: apunta lo que se le pide y falla cuando se le dice.</summary>
public sealed class FakeSync : ISyncService
{
    public bool IsConfigured { get; set; } = true;

    public event EventHandler<RemoteChange>? RemoteChanged;

    public List<string> Calls { get; } = [];

    /// <summary>Si no es null, cualquier llamada al servidor lanza esto.</summary>
    public Exception? Fails { get; set; }

    public bool? Owner { get; set; } = true;

    public GroupInvite Invite { get; set; } = new("ABC123", "clave-compartida");

    private Task Do(string call)
    {
        Calls.Add(call);
        return Fails is { } ex ? Task.FromException(ex) : Task.CompletedTask;
    }

    public void Raise(RemoteChange change) => RemoteChanged?.Invoke(this, change);

    public Task StartAsync(CancellationToken cancellationToken = default) => Do("start");

    public async Task<int> PushAsync(CancellationToken cancellationToken = default)
    {
        await Do("push");
        return 0;
    }

    public Task PullAsync(CancellationToken cancellationToken = default) => Do("pull");

    public async Task<GroupInvite> CreateGroupAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        await Do($"create:{name}");
        return Invite;
    }

    public async Task<Guid> JoinGroupAsync(string joinCode, string sharedKey, CancellationToken cancellationToken = default)
    {
        await Do($"join:{joinCode}:{sharedKey}");
        return Guid.NewGuid();
    }

    public async Task<GroupInvite> RenewInviteAsync(Guid groupId, string joinCode, CancellationToken cancellationToken = default)
    {
        await Do($"renew:{joinCode}");
        return Invite with { JoinCode = joinCode };
    }

    public async Task<bool?> IsGroupOwnerAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        Calls.Add("owner?");
        await Task.Yield();
        return Owner;
    }

    public Task LeaveGroupAsync(Guid groupId, CancellationToken cancellationToken = default) => Do("leave");

    public Task DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken = default) => Do("delete");
}
