using TaskManager.Core.Models;
using TaskManager.Core.Services;

namespace TaskManager.Tests;

/// <summary>Una sincronizacion de mentira que cuenta cuantas veces se le pide y puede quedarse esperando.</summary>
internal sealed class FakeSync : ISyncService
{
    private int _starts;

    public bool IsConfigured { get; set; } = true;
    public int Starts => Volatile.Read(ref _starts);
    public TaskCompletionSource? Hold { get; set; }
    public Exception? Fail { get; set; }

    public event EventHandler<RemoteChange>? RemoteChanged;

    public void Raise(RemoteChange change) => RemoteChanged?.Invoke(this, change);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _starts);
        if (Hold is { } hold)
        {
            await hold.Task.ConfigureAwait(false);
        }

        if (Fail is { } fail)
        {
            throw fail;
        }
    }

    public Task<int> PushAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task PullAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<GroupInvite> CreateGroupAsync(Guid id, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<GroupInvite> RenewInviteAsync(Guid groupId, string joinCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Guid> JoinGroupAsync(string joinCode, string sharedKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool?> IsGroupOwnerAsync(Guid groupId, CancellationToken cancellationToken = default) => Task.FromResult<bool?>(null);
    public Task LeaveGroupAsync(Guid groupId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class SyncCoordinatorTests
{
    private static async Task WaitFor(Func<bool> condition, int ms = 8000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition() && DateTime.UtcNow < until)
        {
            await Task.Delay(20);
        }

        Assert.True(condition(), "No ha pasado a tiempo.");
    }

    [Fact]
    public async Task NothingHappens_WhenSignedOutOrNotConfigured()
    {
        await using var rig = await AuthTests.Rig.CreateAsync("sub-123");
        var sync = new FakeSync();
        using var coordinator = new SyncCoordinator(sync, rig.Auth, rig.Store.Repository, rig.Store.Settings);

        await coordinator.SyncNowAsync();
        await coordinator.RefreshNowAsync();
        Assert.Equal(0, sync.Starts);

        sync.IsConfigured = false;
        await rig.Auth.SignInLocallyAsync();
        await coordinator.SyncNowAsync();
        await coordinator.RefreshNowAsync();
        coordinator.NotifyLocalChange();
        Assert.Equal(0, sync.Starts);
    }

    [Fact]
    public async Task SigningIn_Syncs_AndRaisesChanged()
    {
        await using var rig = await AuthTests.Rig.CreateAsync();
        var sync = new FakeSync();
        using var coordinator = new SyncCoordinator(sync, rig.Auth, rig.Store.Repository, rig.Store.Settings);
        var changed = 0;
        coordinator.Changed += (_, _) => Interlocked.Increment(ref changed);

        await rig.Auth.SignInLocallyAsync(); // UserChanged arranca una sincronizacion
        await WaitFor(() => sync.Starts >= 1 && Volatile.Read(ref changed) >= 1);

        await rig.Auth.SignOutAsync(); // salir no sincroniza
        await Task.Delay(100);
        Assert.Equal(1, sync.Starts);
    }

    [Fact]
    public async Task Failures_AreSwallowed()
    {
        await using var rig = await AuthTests.Rig.CreateAsync();
        await rig.Auth.SignInLocallyAsync();
        var sync = new FakeSync { Fail = new HttpRequestException("sin red") };
        using var coordinator = new SyncCoordinator(sync, rig.Auth, rig.Store.Repository, rig.Store.Settings);

        await coordinator.SyncNowAsync();
        await coordinator.RefreshNowAsync();
        Assert.Equal(2, sync.Starts);
    }

    [Fact]
    public async Task ARequestDuringASync_IsRepeatedAfterwards_NotLost()
    {
        await using var rig = await AuthTests.Rig.CreateAsync();
        await rig.Auth.SignInLocallyAsync();
        var sync = new FakeSync { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var coordinator = new SyncCoordinator(sync, rig.Auth, rig.Store.Repository, rig.Store.Settings);

        var running = coordinator.SyncNowAsync();
        await WaitFor(() => sync.Starts == 1);

        await coordinator.SyncNowAsync(); // ocupado: se apunta y vuelve en el acto
        var refresh = coordinator.RefreshNowAsync(); // el boton espera su turno
        Assert.False(refresh.IsCompleted);

        sync.Hold.SetResult();
        await running;
        await refresh;

        // La primera, su repeticion y la del boton.
        Assert.Equal(3, sync.Starts);
    }

    [Fact]
    public async Task LocalChanges_AreGroupedIntoOneUpload()
    {
        await using var rig = await AuthTests.Rig.CreateAsync();
        await rig.Auth.SignInLocallyAsync();
        var sync = new FakeSync();
        using var coordinator = new SyncCoordinator(sync, rig.Auth, rig.Store.Repository, rig.Store.Settings);

        // Tres cambios seguidos en el repositorio: una sola subida, cuatro segundos despues del ultimo.
        var list = await rig.Store.Repository.CreateListAsync("L");
        await rig.Store.Repository.AddTaskAsync(list.Id, "a");
        await rig.Store.Repository.AddTaskAsync(list.Id, "b");
        Assert.Equal(0, sync.Starts);

        await WaitFor(() => sync.Starts == 1, 10000);
        await Task.Delay(300);
        Assert.Equal(1, sync.Starts);
    }

    [Fact]
    public async Task Backfill_And_Encrypt_QueueEverythingOncePerUser()
    {
        await using var rig = await AuthTests.SignedInAsync();
        var repo = rig.Store.Repository;
        var list = await repo.CreateListAsync("antes de la cuenta");
        await repo.AddTaskAsync(list.Id, "vieja");
        await repo.ClearSyncAsync(await repo.GetPendingSyncAsync());

        if (rig.Auth.CurrentUser is null)
        {
            return; // sin cliente de Google no hay uid del servidor en el usuario
        }

        var sync = new FakeSync();
        using var coordinator = new SyncCoordinator(sync, rig.Auth, repo, rig.Store.Settings);
        await coordinator.SyncNowAsync();

        // Una vez para el volcado inicial y otra para el re-cifrado: dos filas por cada cosa.
        Assert.Equal(4, (await repo.GetPendingSyncAsync()).Count);
        Assert.Equal("sub-123", rig.Store.Settings.Get("sync.backfilled_for"));
        Assert.Equal("sub-123", rig.Store.Settings.Get("sync.encrypted_v1_for"));

        await repo.ClearSyncAsync(await repo.GetPendingSyncAsync());
        await coordinator.SyncNowAsync();
        Assert.Empty(await repo.GetPendingSyncAsync());
    }

    [Fact]
    public async Task RemoteArrivals_AnnounceOnlyNewPendingTasks_Once()
    {
        await using var rig = await AuthTests.Rig.CreateAsync("sub-123");
        var repo = rig.Store.Repository;
        var list = await repo.CreateListAsync("L");
        var fresh = await repo.AddTaskAsync(list.Id, "nueva de otro");
        var done = await repo.AddTaskAsync(list.Id, "ya hecha");
        done.IsDone = true;
        await repo.UpdateTaskAsync(done);

        var sync = new FakeSync();
        using var coordinator = new SyncCoordinator(sync, rig.Auth, repo, rig.Store.Settings);
        var arrived = new List<ArrivedTask>();
        var changed = 0;
        coordinator.TaskArrived += (_, a) => { lock (arrived) { arrived.Add(a); } };
        coordinator.Changed += (_, _) => Interlocked.Increment(ref changed);

        sync.Raise(new RemoteChange("tasks", fresh.Id.ToString(), IsNew: true));
        sync.Raise(new RemoteChange("tasks", fresh.Id.ToString(), IsNew: true));
        sync.Raise(new RemoteChange("tasks", fresh.Id.ToString(), IsNew: false));
        sync.Raise(new RemoteChange("tasks", done.Id.ToString(), IsNew: true));
        sync.Raise(new RemoteChange("tasks", Guid.NewGuid().ToString(), IsNew: true));
        sync.Raise(new RemoteChange("tasks", "no-es-guid", IsNew: true));
        sync.Raise(new RemoteChange("task_steps", Guid.NewGuid().ToString(), IsNew: true));

        await WaitFor(() => Volatile.Read(ref changed) == 7);
        await Task.Delay(300);
        Assert.Equal([new ArrivedTask(fresh.Id, "nueva de otro")], arrived);
    }

    [Fact]
    public async Task StartStopDispose()
    {
        await using var rig = await AuthTests.Rig.CreateAsync();
        await rig.Auth.SignInLocallyAsync();
        var sync = new FakeSync();
        var coordinator = new SyncCoordinator(sync, rig.Auth, rig.Store.Repository, rig.Store.Settings);

        coordinator.Start();
        await WaitFor(() => sync.Starts == 1);
        coordinator.Start(); // el temporizador no se duplica
        await WaitFor(() => sync.Starts == 2);
        coordinator.Stop();
        coordinator.Stop();

        coordinator.NotifyLocalChange();
        coordinator.Dispose();
        await coordinator.SyncNowAsync();
        await coordinator.RefreshNowAsync();
        coordinator.NotifyLocalChange();
        await Task.Delay(4500);
        Assert.Equal(2, sync.Starts);
    }
}
