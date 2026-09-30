using System.Net;
using System.Text.Json;
using TaskManager.Core;
using TaskManager.Core.Models;
using TaskManager.Core.Services;
using static TaskManager.Tests.AuthTests;

namespace TaskManager.Tests;

public class SupabaseSyncTests
{
    private const string Rest = "/rest/v1/";

    private sealed class SyncRig : IAsyncDisposable
    {
        public required Rig Inner { get; init; }
        public required SupabaseSyncService Sync { get; init; }
        public TestStore Store => Inner.Store;
        public FakeHttp Http => Inner.Http;
        public List<RemoteChange> Changes { get; } = [];

        public static async Task<SyncRig> CreateAsync()
        {
            var inner = await SignedInAsync();
            var sync = new SupabaseSyncService(inner.Http.Client(), inner.Store.Repository, inner.Store.Settings, inner.Auth);
            var rig = new SyncRig { Inner = inner, Sync = sync };
            sync.RemoteChanged += (_, c) => rig.Changes.Add(c);

            // Por defecto el servidor lo acepta todo y no tiene nada.
            inner.Http.Fallback = HttpStatusCode.NotFound;
            inner.Http.On(HttpMethod.Post, Rest, HttpStatusCode.Created);
            inner.Http.On(HttpMethod.Delete, Rest, HttpStatusCode.NoContent);
            inner.Http.On(HttpMethod.Patch, Rest, HttpStatusCode.NoContent);
            inner.Http.On(HttpMethod.Get, Rest, HttpStatusCode.OK, "[]");
            return rig;
        }

        public ValueTask DisposeAsync() => Inner.DisposeAsync();

        public List<JsonElement> Posted(string table) => Http.Calls
            .Where(c => c.Method == HttpMethod.Post && c.Url.EndsWith(Rest + table, StringComparison.Ordinal))
            .SelectMany(c => JsonDocument.Parse(c.Body).RootElement.EnumerateArray().Select(e => e.Clone()))
            .ToList();
    }

    private static string Decrypt(string stored, params string[] keys) => new TextCipher().Unprotect(stored, keys);

    // -----------------------------------------------------------------------
    // Subida
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IsConfigured_FollowsEmbeddedConfig()
    {
        await using var store = await TestStore.CreateAsync();
        var auth = new SupabaseAuthService(new FakeHttp().Client(), store.Settings, new FakeTokens(), new FakeBrowser());
        Assert.Equal(SupabaseConfig.IsConfigured, new SupabaseSyncService(new FakeHttp().Client(), store.Repository, store.Settings, auth).IsConfigured);
    }

    [Fact]
    public async Task Push_WithoutSession_DoesNothing()
    {
        await using var s = await TestStore.CreateAsync();
        var http = new FakeHttp();
        var auth = new SupabaseAuthService(http.Client(), s.Settings, new FakeTokens(), new FakeBrowser());
        var sync = new SupabaseSyncService(http.Client(), s.Repository, s.Settings, auth);
        await s.Repository.CreateListAsync("L");

        Assert.Equal(0, await sync.PushAsync());
        await sync.PullAsync();
        Assert.Empty(http.Calls);
        Assert.Single(await s.Repository.GetPendingSyncAsync());
    }

    [Fact]
    public async Task Push_EncryptsFreeText_WithUserOrGroupKey_AndEmptiesTheQueue()
    {
        await using var rig = await SyncRig.CreateAsync();
        var repo = rig.Store.Repository;
        var list = await repo.CreateListAsync("Personal");
        var group = await repo.SaveGroupAsync(new TaskGroup { Name = "Casa" });
        var shared = await repo.CreateListAsync("Compra", group.Id);
        var task = await repo.AddTaskAsync(list.Id, "Llamar al banco");
        task.Notes = "antes de las 14";
        task.Tags = ",banco,";
        task.MyDayOn = new DateTime(2026, 9, 29);
        await repo.UpdateTaskAsync(task); // dos cambios de la misma tarea: se sube una vez
        var sharedTask = await repo.AddTaskAsync(shared.Id, "Leche");
        await repo.AddStepsAsync(sharedTask.Id, ["entera"]);
        await repo.AddFileAsync(task.Id, "a.bin", [0xAB, 0x01]);
        await repo.AddLinkAsync(task.Id, "ejemplo.com");

        var pushed = await rig.Sync.PushAsync();

        Assert.Empty((await rig.Store.Db.Connection.Table<SyncOp>().ToListAsync()).Select(o => $"{o.AccountId}|{o.Entity}|{o.Operation}"));
        Assert.True(pushed >= 8);

        var tasks = rig.Posted("tasks");
        Assert.Equal(2, tasks.Count);
        var mine = tasks.Single(t => t.GetProperty("id").GetGuid() == task.Id);
        Assert.StartsWith("enc1:", mine.GetProperty("title").GetString());
        Assert.Equal("Llamar al banco", Decrypt(mine.GetProperty("title").GetString()!, RemoteId));
        Assert.Equal("antes de las 14", Decrypt(mine.GetProperty("notes").GetString()!, RemoteId));
        Assert.Equal("2026-09-29", mine.GetProperty("my_day_on").GetString());
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("due_at").ValueKind); // los nulos se escriben
        Assert.Equal(RemoteId, mine.GetProperty("created_by").GetString());

        var theirs = tasks.Single(t => t.GetProperty("id").GetGuid() == sharedTask.Id);
        Assert.Equal("Leche", Decrypt(theirs.GetProperty("title").GetString()!, group.Id.ToString()));
        Assert.Equal(theirs.GetProperty("title").GetString(), Decrypt(theirs.GetProperty("title").GetString()!, RemoteId));

        var lists = rig.Posted("task_lists");
        Assert.Equal("Compra", Decrypt(lists.Single(l => l.GetProperty("id").GetGuid() == shared.Id).GetProperty("name").GetString()!, group.Id.ToString()));
        Assert.Equal("entera", Decrypt(rig.Posted("task_steps").Single().GetProperty("title").GetString()!, group.Id.ToString()));

        var attachments = rig.Posted("task_attachments");
        Assert.Equal("\\xab01", attachments.Single(a => a.GetProperty("kind").GetString() == "file").GetProperty("data").GetString());
        Assert.Equal(JsonValueKind.Null, attachments.Single(a => a.GetProperty("kind").GetString() == "url").GetProperty("data").ValueKind);

        var request = rig.Http.To(Rest + "tasks").First().Request;
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Contains("resolution=merge-duplicates", request.Headers.GetValues("Prefer").Single());
    }

    /// <summary>
    /// Fallo encontrado por esta prueba (2026-09-30): de una tarea tocada varias veces sin conexion
    /// solo salia de la cola la ultima entrada, y se volvia a subir en cada vuelta siguiente.
    /// </summary>
    [Fact]
    public async Task Push_ManyEditsOfTheSameTask_UploadOnce_AndLeaveNothingBehind()
    {
        await using var rig = await SyncRig.CreateAsync();
        var repo = rig.Store.Repository;
        var list = await repo.CreateListAsync("L");
        var t = await repo.AddTaskAsync(list.Id, "v1");
        for (var i = 2; i <= 5; i++)
        {
            t.Title = $"v{i}";
            await repo.UpdateTaskAsync(t);
        }

        await rig.Sync.PushAsync();
        await rig.Sync.PushAsync();
        await rig.Sync.PushAsync();

        var uploaded = Assert.Single(rig.Posted("tasks"));
        Assert.Equal("v5", Decrypt(uploaded.GetProperty("title").GetString()!, RemoteId));
        Assert.Empty(await repo.GetPendingSyncAsync());
    }

    [Fact]
    public async Task Push_Profile_OnlyWhenItChanges()
    {
        await using var rig = await SyncRig.CreateAsync();
        if (rig.Inner.Auth.CurrentUser is null)
        {
            return; // sin cliente de Google no hay perfil que subir
        }

        await rig.Sync.PushAsync();
        await rig.Sync.PushAsync();

        var profile = Assert.Single(rig.Posted("profiles"));
        Assert.Equal("Ana", Decrypt(profile.GetProperty("display_name").GetString()!, RemoteId));
        Assert.Equal("ana@example.com", Decrypt(profile.GetProperty("email").GetString()!, RemoteId));
    }

    [Fact]
    public async Task Push_Deletes_NoteTheDeletionFirst()
    {
        await using var rig = await SyncRig.CreateAsync();
        var repo = rig.Store.Repository;
        var list = await repo.CreateListAsync("L");
        var t = await repo.AddTaskAsync(list.Id, "t");
        await repo.DeleteTaskAsync(t);

        await rig.Sync.PushAsync();

        var deletion = Assert.Single(rig.Posted("deletions"));
        Assert.Equal("tasks", deletion.GetProperty("entity").GetString());
        Assert.Equal(t.Id, deletion.GetProperty("entity_id").GetGuid());
        Assert.Single(rig.Http.Calls, c => c.Method == HttpMethod.Delete && c.Url.EndsWith($"tasks?id=eq.{t.Id}"));
        Assert.Empty(await repo.GetPendingSyncAsync());
    }

    [Theory]
    [InlineData("deletions-rejected")]
    [InlineData("delete-rejected")]
    [InlineData("delete-offline")]
    public async Task Push_DeleteThatFails_StaysQueued(string failure)
    {
        await using var rig = await SyncRig.CreateAsync();
        var repo = rig.Store.Repository;
        var list = await repo.CreateListAsync("L");
        await repo.DeleteListAsync(list);
        await repo.ClearSyncAsync((await repo.GetPendingSyncAsync()).Where(o => o.Operation != "delete"));

        switch (failure)
        {
            case "deletions-rejected":
                rig.Http.On(HttpMethod.Post, Rest + "deletions", HttpStatusCode.Forbidden, "rls");
                break;
            case "delete-rejected":
                rig.Http.On(HttpMethod.Delete, Rest + "task_lists", HttpStatusCode.Conflict, "fk");
                break;
            default:
                rig.Http.Throw(HttpMethod.Delete, Rest + "task_lists");
                break;
        }

        Assert.Equal(0, await rig.Sync.PushAsync());
        Assert.Single(await repo.GetPendingSyncAsync());
        if (failure != "delete-offline")
        {
            Assert.NotNull(rig.Sync.LastError);
            Assert.Contains(rig.Sync.LastError!.Split(':')[0], rig.Store.Settings.Get(SupabaseSyncService.KeyLastError));
        }
    }

    [Fact]
    public async Task Push_Rejected_KeepsTheQueue_AndSaysWhy()
    {
        await using var rig = await SyncRig.CreateAsync();
        var repo = rig.Store.Repository;
        await repo.CreateListAsync("L");
        rig.Http.On(HttpMethod.Post, Rest + "task_lists", HttpStatusCode.BadRequest, "PGRST102: All object keys must match");

        Assert.Equal(0, await rig.Sync.PushAsync());
        Assert.Single(await repo.GetPendingSyncAsync());
        Assert.Equal("task_lists: 400 PGRST102: All object keys must match", rig.Sync.LastError);
        Assert.EndsWith("PGRST102: All object keys must match", rig.Store.Settings.Get(SupabaseSyncService.KeyLastError));

        rig.Http.Throw(HttpMethod.Post, Rest + "task_lists", new TaskCanceledException("tiempo"));
        Assert.Equal(0, await rig.Sync.PushAsync());
        Assert.Equal("task_lists: TaskCanceledException tiempo", rig.Sync.LastError);
    }

    [Fact]
    public async Task Push_RowsThatCanNeverBeBuilt_LeaveTheQueue()
    {
        await using var rig = await SyncRig.CreateAsync();
        var db = rig.Store.Db.Connection;
        foreach (var (entity, id) in new[]
        {
            ("tasks", Guid.NewGuid().ToString()),
            ("task_lists", Guid.NewGuid().ToString()),
            ("task_steps", Guid.NewGuid().ToString()),
            ("task_attachments", Guid.NewGuid().ToString()),
            ("xp_events", Guid.NewGuid().ToString()),
            ("tasks", "no-es-un-guid"),
        })
        {
            await db.InsertAsync(new SyncOp { AccountId = "sub-123", Entity = entity, EntityId = id });
        }

        await db.InsertAsync(new SyncOp { AccountId = "sub-123", Entity = "tasks", EntityId = "roto", Operation = "delete" });

        await rig.Sync.PushAsync();

        Assert.Equal(["roto"], (await rig.Store.Repository.GetPendingSyncAsync()).Select(o => o.EntityId));
        Assert.Empty(rig.Posted("tasks"));
    }

    [Fact]
    public async Task Push_StepOrAttachmentOfAMissingTask_UsesTheUserKey()
    {
        await using var rig = await SyncRig.CreateAsync();
        var db = rig.Store.Db.Connection;
        var step = new TaskStep { TaskId = Guid.NewGuid(), Title = "huerfano" };
        await db.InsertAsync(step);
        await db.InsertAsync(new SyncOp { AccountId = "sub-123", Entity = "task_steps", EntityId = step.Id.ToString() });
        var task = new TaskItem { ListId = Guid.NewGuid(), Title = "sin lista", AccountId = "sub-123" };
        await db.InsertAsync(task);
        await db.InsertAsync(new SyncOp { AccountId = "sub-123", Entity = "tasks", EntityId = task.Id.ToString() });

        await rig.Sync.PushAsync();

        Assert.Equal("huerfano", Decrypt(rig.Posted("task_steps").Single().GetProperty("title").GetString()!, RemoteId));
        Assert.Equal("sin lista", Decrypt(rig.Posted("tasks").Single().GetProperty("title").GetString()!, RemoteId));
    }

    // -----------------------------------------------------------------------
    // Bajada
    // -----------------------------------------------------------------------

    private static string Json(object o) => JsonSerializer.Serialize(o);

    private static object TaskRow(Guid id, Guid listId, string title, DateTime updated, DateTimeOffset synced, string notes = "", bool done = false) => new
    {
        id,
        list_id = listId,
        title,
        notes,
        is_pinned = true,
        is_done = done,
        in_progress = false,
        series_id = (Guid?)null,
        done_at = (DateTimeOffset?)null,
        my_day_on = "2026-09-29",
        due_at = "2026-10-01T10:00:00+00:00",
        planned_for = "2026-09-30",
        tags = ",x,",
        recurrence_rule = "weekly:1",
        sort_order = 3,
        updated_at = updated,
        deleted = false,
        synced_at = synced,
    };

    [Fact]
    public async Task Pull_MergesDecrypts_AndMovesTheCutToWhatArrived()
    {
        await using var rig = await SyncRig.CreateAsync();
        var repo = rig.Store.Repository;
        var cipher = new TextCipher();
        var listId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var stepId = Guid.NewGuid();
        var attId = Guid.NewGuid();
        var synced = new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
        var updated = new DateTime(2026, 9, 29, 9, 0, 0, DateTimeKind.Utc);

        rig.Http.On(HttpMethod.Get, Rest + "task_lists?", HttpStatusCode.OK, Json(new[]
        {
            new { id = listId, group_id = (Guid?)null, owner_id = Guid.Parse(RemoteId), name = cipher.Protect("Remota", RemoteId), icon = "ic_list", sort_order = 1, updated_at = updated, deleted = false, synced_at = synced },
        }));
        rig.Http.On(HttpMethod.Get, Rest + "tasks?", HttpStatusCode.OK, Json(new[]
        {
            TaskRow(taskId, listId, cipher.Protect("Desde el móvil", RemoteId), updated, synced.AddSeconds(5), notes: "en claro"),
        }));
        rig.Http.On(HttpMethod.Get, Rest + "task_steps?", HttpStatusCode.OK, Json(new[]
        {
            new { id = stepId, task_id = taskId, title = cipher.Protect("paso", RemoteId), is_done = true, sort_order = 0, source = "manual", updated_at = updated, deleted = false, synced_at = synced },
        }));
        rig.Http.On(HttpMethod.Get, Rest + "task_attachments?", HttpStatusCode.OK, Json(new object[]
        {
            new { id = attId, task_id = taskId, kind = "file", name = cipher.Protect("f.txt", RemoteId), url = "", data = "\\x6869", sort_order = 0, updated_at = synced, deleted = false, synced_at = synced },
            new { id = Guid.NewGuid(), task_id = taskId, kind = "file", name = "roto", url = "", data = "\\xZZ", sort_order = 1, updated_at = synced, deleted = false, synced_at = synced },
            new { id = Guid.NewGuid(), task_id = taskId, kind = "url", name = "enlace", url = "https://a", data = (string?)null, sort_order = 2, updated_at = synced, deleted = false, synced_at = synced },
        }));

        await rig.Sync.PullAsync();

        var list = (await repo.GetListAsync(listId))!;
        Assert.Equal("Remota", list.Name);
        Assert.Equal("sub-123", list.AccountId);
        Assert.Equal(RemoteId, list.OwnerId);

        var task = (await repo.GetTaskAsync(taskId))!;
        Assert.Equal("Desde el móvil", task.Title);
        Assert.Equal("en claro", task.Notes);
        Assert.True(task.IsPinned);
        Assert.Equal(new DateTime(2026, 9, 30), task.PlannedFor);
        Assert.Equal(new DateTime(2026, 9, 29), task.MyDayOn);
        Assert.Equal(RecurrenceKind.Weekly, task.Recurrence.Kind);
        Assert.Equal("paso", (await repo.GetStepAsync(stepId))!.Title);

        var attachments = await repo.GetAttachmentsAsync(taskId);
        Assert.Equal("hi"u8.ToArray(), attachments[0].Data);
        Assert.Null(attachments[1].Data);
        Assert.Null(attachments[2].Data);

        Assert.Contains(rig.Changes, c => c.Entity == "tasks" && c.IsNew && c.EntityId == taskId.ToString());
        Assert.Equal(3, rig.Changes.Count(c => c.Entity == "task_attachments"));
        Assert.Empty(await repo.GetPendingSyncAsync()); // lo que baja no vuelve a subir

        // El corte es la marca mas alta que llego, y la siguiente vez se pregunta desde ahi.
        await rig.Sync.PullAsync();
        var second = rig.Http.To(Rest + "tasks?").Last().Url;
        Assert.Contains("synced_at=gt.2026-09-29T10%3A00%3A05", second);
        Assert.Contains("deleted_at=gt.", rig.Http.To(Rest + "deletions?").Last().Url);
    }

    [Fact]
    public async Task Pull_LocalNewerWins_UnlessLocalIsStillEncrypted()
    {
        await using var rig = await SyncRig.CreateAsync();
        var repo = rig.Store.Repository;
        var list = await repo.CreateListAsync("L");
        var fresh = await repo.AddTaskAsync(list.Id, "editada aqui");
        var locked = await repo.AddTaskAsync(list.Id, "enc1:AAAA");
        var old = DateTime.UtcNow.AddDays(-1);
        var synced = DateTimeOffset.UtcNow;

        rig.Http.On(HttpMethod.Get, Rest + "tasks?", HttpStatusCode.OK, Json(new[]
        {
            TaskRow(fresh.Id, list.Id, "vieja del servidor", old, synced),
            TaskRow(locked.Id, list.Id, "ya legible", old, synced),
        }));

        await rig.Sync.PullAsync();

        Assert.Equal("editada aqui", (await repo.GetTaskAsync(fresh.Id))!.Title);
        Assert.Equal("ya legible", (await repo.GetTaskAsync(locked.Id))!.Title);
        Assert.Contains(rig.Changes, c => c.EntityId == locked.Id.ToString() && !c.IsNew);
    }

    [Fact]
    public async Task Pull_NewerRemoteWins_ForListsStepsAndAttachments()
    {
        await using var rig = await SyncRig.CreateAsync();
        var repo = rig.Store.Repository;
        var list = await repo.CreateListAsync("vieja");
        var t = await repo.AddTaskAsync(list.Id, "t");
        var step = (await repo.AddStepsAsync(t.Id, ["viejo"]))[0];
        var link = await repo.AddLinkAsync(t.Id, "https://viejo");
        var newer = DateTime.UtcNow.AddMinutes(5);
        var older = DateTime.UtcNow.AddDays(-5);
        var synced = DateTimeOffset.UtcNow;

        rig.Http.On(HttpMethod.Get, Rest + "task_lists?", HttpStatusCode.OK, Json(new[]
        {
            new { id = list.Id, group_id = (Guid?)null, owner_id = (Guid?)null, name = "nueva", icon = "ic_star", sort_order = 0, updated_at = newer, deleted = false, synced_at = synced },
        }));
        rig.Http.On(HttpMethod.Get, Rest + "task_steps?", HttpStatusCode.OK, Json(new[]
        {
            new { id = step.Id, task_id = t.Id, title = "nuevo", is_done = false, sort_order = 0, source = "manual", updated_at = newer, deleted = false, synced_at = synced },
        }));
        rig.Http.On(HttpMethod.Get, Rest + "task_attachments?", HttpStatusCode.OK, Json(new[]
        {
            new { id = link.Id, task_id = t.Id, kind = "url", name = "no pisa", url = "https://no", data = (string?)null, sort_order = 0, updated_at = new DateTimeOffset(older), deleted = false, synced_at = synced },
        }));

        await rig.Sync.PullAsync();

        Assert.Equal("nueva", (await repo.GetListAsync(list.Id))!.Name);
        Assert.Equal(string.Empty, (await repo.GetListAsync(list.Id))!.OwnerId);
        Assert.Equal("nuevo", (await repo.GetStepAsync(step.Id))!.Title);
        Assert.Equal("https://viejo", (await repo.GetAttachmentAsync(link.Id))!.Url);

        // Una segunda vuelta con lo mismo no reescribe nada (margen de un milisegundo).
        rig.Changes.Clear();
        await rig.Sync.PullAsync();
        Assert.Empty(rig.Changes);
    }

    [Fact]
    public async Task Pull_AnyFetchFailing_AppliesNothing()
    {
        await using var rig = await SyncRig.CreateAsync();
        var repo = rig.Store.Repository;
        var list = await repo.CreateListAsync("L");
        rig.Http.On(HttpMethod.Get, Rest + "deletions?", HttpStatusCode.OK,
            Json(new[] { new { entity = "task_lists", entity_id = list.Id, deleted_at = DateTimeOffset.UtcNow } }));
        rig.Http.On(HttpMethod.Get, Rest + "task_steps?", HttpStatusCode.ServiceUnavailable);

        await rig.Sync.PullAsync();
        Assert.NotNull(await repo.GetListAsync(list.Id));

        rig.Http.Throw(HttpMethod.Get, Rest + "task_steps?");
        await rig.Sync.PullAsync();
        rig.Http.Throw(HttpMethod.Get, Rest + "task_steps?", new TaskCanceledException());
        await rig.Sync.PullAsync();
        Assert.NotNull(await repo.GetListAsync(list.Id));

        rig.Http.On(HttpMethod.Get, Rest + "task_steps?", HttpStatusCode.OK, "[]");
        await rig.Sync.PullAsync();
        Assert.Null(await repo.GetListAsync(list.Id));
        Assert.Contains(rig.Changes, c => c.Entity == "task_lists");
    }

    [Fact]
    public async Task Pull_GroupsGoneFromTheServer_AreRemovedHere()
    {
        await using var rig = await SyncRig.CreateAsync();
        var repo = rig.Store.Repository;
        var alive = await repo.SaveGroupAsync(new TaskGroup { Name = "sigue" });
        var gone = await repo.SaveGroupAsync(new TaskGroup { Name = "borrado" });
        rig.Http.On(HttpMethod.Get, Rest + "groups?", HttpStatusCode.OK, Json(new[] { new { id = alive.Id } }));

        await rig.Sync.PullAsync();

        Assert.Equal([alive.Id], (await repo.GetGroupsAsync()).Select(g => g.Id));
        Assert.Contains(rig.Changes, c => c.Entity == "groups" && c.EntityId == gone.Id.ToString());
    }

    [Fact]
    public async Task Start_PushesThenPulls()
    {
        await using var rig = await SyncRig.CreateAsync();
        await rig.Store.Repository.CreateListAsync("L");
        await rig.Sync.StartAsync();

        var post = rig.Http.Calls.FindIndex(c => c.Method == HttpMethod.Post && c.Url.EndsWith("task_lists"));
        var get = rig.Http.Calls.FindIndex(c => c.Method == HttpMethod.Get && c.Url.Contains("task_lists?"));
        Assert.True(post >= 0 && get > post);
    }

    // -----------------------------------------------------------------------
    // Grupos
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateGroup_SendsEncryptedName_ReturnsCodeAndFreshKey()
    {
        await using var rig = await SyncRig.CreateAsync();
        var id = Guid.NewGuid();
        rig.Http.On(HttpMethod.Post, "rpc/create_group", HttpStatusCode.OK, Json(new[] { new { group_id = id, join_code = "ABC123" } }));

        var invite = await rig.Sync.CreateGroupAsync(id, "Familia");

        Assert.Equal("ABC123", invite.JoinCode);
        Assert.True(Guid.TryParse(invite.SharedKey, out _));
        var body = JsonDocument.Parse(rig.Http.To("rpc/create_group").Single().Body).RootElement;
        Assert.Equal("Familia", Decrypt(body.GetProperty("p_name").GetString()!, id.ToString()));
        Assert.Equal(invite.SharedKey, body.GetProperty("p_key").GetString());

        rig.Http.On(HttpMethod.Post, "rpc/create_group", HttpStatusCode.OK, "[]");
        Assert.Equal(string.Empty, (await rig.Sync.CreateGroupAsync(Guid.NewGuid(), "x")).JoinCode);

        rig.Http.On(HttpMethod.Post, "rpc/create_group", HttpStatusCode.NotFound, "Could not find the function");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sync.CreateGroupAsync(Guid.NewGuid(), "x"));
        Assert.Equal("create_group (404): Could not find the function", ex.Message);
    }

    [Fact]
    public async Task RenewInvite_KeepsCode_OwnerOnly()
    {
        await using var rig = await SyncRig.CreateAsync();
        rig.Http.On(HttpMethod.Post, "rpc/rotate_group_key", HttpStatusCode.NoContent);
        var invite = await rig.Sync.RenewInviteAsync(Guid.NewGuid(), "ABC123");
        Assert.Equal("ABC123", invite.JoinCode);

        rig.Http.On(HttpMethod.Post, "rpc/rotate_group_key", HttpStatusCode.BadRequest, "{\"message\":\"Not the owner\"}");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sync.RenewInviteAsync(Guid.NewGuid(), "A"));
        Assert.Equal("Solo quien creo el grupo puede repartir invitaciones.", ex.Message);

        rig.Http.On(HttpMethod.Post, "rpc/rotate_group_key", HttpStatusCode.InternalServerError, "otro");
        Assert.Equal("otro", (await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sync.RenewInviteAsync(Guid.NewGuid(), "A"))).Message);
    }

    [Fact]
    public async Task JoinGroup_SavesTheGroupWithItsRealName_AndRestartsThePull()
    {
        await using var rig = await SyncRig.CreateAsync();
        var id = Guid.NewGuid();
        await rig.Store.Settings.SetAsync("sync.last_pull_v3.sub-123", "2026-01-01T00:00:00Z");
        rig.Http.On(HttpMethod.Post, "rpc/join_group", HttpStatusCode.OK, $"\"{id}\"");
        rig.Http.On(HttpMethod.Get, $"groups?id=eq.{id}&select=name", HttpStatusCode.OK,
            Json(new[] { new { name = new TextCipher().Protect("Piso", id.ToString()), join_code = "XYZ789" } }));

        Assert.Equal(id, await rig.Sync.JoinGroupAsync(" xyz789 ", "clave"));

        var group = (await rig.Store.Repository.GetGroupAsync(id))!;
        Assert.Equal("Piso", group.Name);
        Assert.Equal("XYZ789", group.JoinCode);
        Assert.Equal(string.Empty, rig.Store.Settings.Get("sync.last_pull_v3.sub-123"));

        var patch = rig.Http.Calls.Single(c => c.Method == HttpMethod.Patch);
        Assert.Contains($"user_id=eq.{RemoteId}", patch.Url);
    }

    [Fact]
    public async Task JoinGroup_WithoutTheGroupCard_UsesTheCodeAsName()
    {
        await using var rig = await SyncRig.CreateAsync();
        var id = Guid.NewGuid();
        rig.Http.On(HttpMethod.Post, "rpc/join_group", HttpStatusCode.OK, id.ToString());
        rig.Http.On(HttpMethod.Get, $"groups?id=eq.{id}", HttpStatusCode.OK, "no es json");
        rig.Http.On(HttpMethod.Patch, "group_members", HttpStatusCode.Forbidden, "rls");

        await rig.Sync.JoinGroupAsync("abc", "k");

        Assert.Equal("ABC", (await rig.Store.Repository.GetGroupAsync(id))!.Name);
        Assert.StartsWith("group_members: 403", rig.Sync.LastError);

        var id2 = Guid.NewGuid();
        rig.Http.On(HttpMethod.Post, "rpc/join_group", HttpStatusCode.OK, id2.ToString());
        rig.Http.Throw(HttpMethod.Patch, "group_members");
        rig.Http.On(HttpMethod.Get, $"groups?id=eq.{id2}", HttpStatusCode.OK, "[]");
        await rig.Sync.JoinGroupAsync("def", "k");
        Assert.Equal("DEF", (await rig.Store.Repository.GetGroupAsync(id2))!.Name);
        Assert.StartsWith("group_members: HttpRequestException", rig.Sync.LastError);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{\"message\":\"invalid code or key\"}", "El código o la clave no son correctos.")]
    [InlineData(HttpStatusCode.InternalServerError, "boom", "join_group (500): boom")]
    public async Task JoinGroup_Rejected(HttpStatusCode status, string body, string expected)
    {
        await using var rig = await SyncRig.CreateAsync();
        rig.Http.On(HttpMethod.Post, "rpc/join_group", status, body);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sync.JoinGroupAsync("A", "B"));
        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public async Task JoinGroup_NotAGuid_IsRejected()
    {
        await using var rig = await SyncRig.CreateAsync();
        rig.Http.On(HttpMethod.Post, "rpc/join_group", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<AuthException>(() => rig.Sync.JoinGroupAsync("A", "B"));
    }

    [Fact]
    public async Task IsGroupOwner()
    {
        await using var rig = await SyncRig.CreateAsync();
        var g = Guid.NewGuid();

        rig.Http.On(HttpMethod.Get, "select=owner_id", HttpStatusCode.OK, Json(new[] { new { owner_id = RemoteId.ToUpperInvariant() } }));
        Assert.True(await rig.Sync.IsGroupOwnerAsync(g));

        rig.Http.On(HttpMethod.Get, "select=owner_id", HttpStatusCode.OK, Json(new[] { new { owner_id = Guid.NewGuid().ToString() } }));
        Assert.False(await rig.Sync.IsGroupOwnerAsync(g));

        rig.Http.On(HttpMethod.Get, "select=owner_id", HttpStatusCode.OK, "[]");
        Assert.True(await rig.Sync.IsGroupOwnerAsync(g));

        rig.Http.On(HttpMethod.Get, "select=owner_id", HttpStatusCode.Unauthorized);
        Assert.Null(await rig.Sync.IsGroupOwnerAsync(g));

        rig.Http.Throw(HttpMethod.Get, "select=owner_id");
        Assert.Null(await rig.Sync.IsGroupOwnerAsync(g));
    }

    [Fact]
    public async Task LeaveAndDeleteGroup()
    {
        await using var rig = await SyncRig.CreateAsync();
        var g = Guid.NewGuid();

        await rig.Sync.LeaveGroupAsync(g);
        await rig.Sync.DeleteGroupAsync(g);
        Assert.Contains(rig.Http.Calls, c => c.Method == HttpMethod.Delete && c.Url.EndsWith($"group_members?group_id=eq.{g}&user_id=eq.{RemoteId}"));
        Assert.Contains(rig.Http.Calls, c => c.Method == HttpMethod.Delete && c.Url.EndsWith($"groups?id=eq.{g}"));

        rig.Http.On(HttpMethod.Delete, Rest + "groups", HttpStatusCode.Forbidden, "no eres el dueño");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sync.DeleteGroupAsync(g));
        Assert.Equal("groups: 403 no eres el dueño", ex.Message);
        Assert.Equal(ex.Message, rig.Sync.LastError);
    }

    [Fact]
    public async Task GroupOperations_WithoutSession()
    {
        await using var s = await TestStore.CreateAsync();
        var http = new FakeHttp();
        var auth = new SupabaseAuthService(http.Client(), s.Settings, new FakeTokens(), new FakeBrowser());
        var sync = new SupabaseSyncService(http.Client(), s.Repository, s.Settings, auth);
        var g = Guid.NewGuid();

        await Assert.ThrowsAsync<AuthException>(() => sync.CreateGroupAsync(g, "x"));
        await Assert.ThrowsAsync<AuthException>(() => sync.RenewInviteAsync(g, "x"));
        await Assert.ThrowsAsync<AuthException>(() => sync.JoinGroupAsync("x", "y"));
        await Assert.ThrowsAsync<AuthException>(() => sync.LeaveGroupAsync(g));
        await Assert.ThrowsAsync<AuthException>(() => sync.DeleteGroupAsync(g));
        Assert.Null(await sync.IsGroupOwnerAsync(g));
        Assert.Empty(http.Calls);
    }
}

public class LocalOnlySyncTests
{
    [Fact]
    public async Task EverythingStaysLocal()
    {
        await using var s = await TestStore.CreateAsync();
        var sync = new LocalOnlySyncService(s.Repository);
        sync.RemoteChanged += (_, _) => { };

        Assert.False(sync.IsConfigured);
        await sync.StartAsync();
        await sync.PullAsync();
        Assert.Equal(0, await sync.PushAsync());

        var invite = await sync.CreateGroupAsync(Guid.NewGuid(), "g");
        Assert.Matches("^[A-HJ-NP-Z2-9]{6}$", invite.JoinCode);
        Assert.True(await sync.IsGroupOwnerAsync(Guid.NewGuid()));
        await sync.LeaveGroupAsync(Guid.NewGuid());
        await sync.DeleteGroupAsync(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => { _ = sync.RenewInviteAsync(Guid.NewGuid(), "A"); });
        Assert.Throws<InvalidOperationException>(() => { _ = sync.JoinGroupAsync("A", "B"); });
    }
}
