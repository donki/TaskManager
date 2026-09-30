using TaskManager.Core.Data;
using TaskManager.Core.Models;
using TaskManager.Core.Services;

namespace TaskManager.Tests;

public class RepositoryTests
{
    private static async Task<List<SyncOp>> QueueOf(TestStore s) =>
        await s.Db.Connection.Table<SyncOp>().ToListAsync();

    [Fact]
    public async Task Lists_CreateUpdateOrder_AndQueue()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;

        var a = await repo.CreateListAsync("  Casa  ");
        var b = await repo.CreateListAsync("Trabajo", icon: "ic_work");
        Assert.Equal("Casa", a.Name);
        Assert.Equal("cuenta-a", a.AccountId);
        Assert.True(b.SortOrder > a.SortOrder);

        a.Name = "Hogar";
        await repo.UpdateListAsync(a);
        Assert.Equal("Hogar", (await repo.GetListAsync(a.Id))!.Name);

        Assert.Equal(["Hogar", "Trabajo"], (await repo.GetPrivateListsAsync()).Select(l => l.Name));
        Assert.Equal(a.Id, (await repo.GetOrCreateDefaultListAsync("X")).Id);

        var ops = await QueueOf(s);
        Assert.Equal(3, ops.Count(o => o.Entity == "task_lists" && o.Operation == "upsert"));
    }

    [Fact]
    public async Task DefaultList_IsCreatedWhenThereIsNone()
    {
        await using var s = await TestStore.CreateAsync();
        var list = await s.Repository.GetOrCreateDefaultListAsync("Tasks");
        Assert.Equal("Tasks", list.Name);
    }

    [Fact]
    public async Task Accounts_DoNotSeeEachOther()
    {
        await using var s = await TestStore.CreateAsync();
        var mine = await s.Repository.CreateListAsync("Mia");
        await s.Repository.AddTaskAsync(mine.Id, "de A");

        await s.Settings.SetAsync(SettingsService.KeyGoogleSub, "cuenta-b");
        Assert.Empty(await s.Repository.GetPrivateListsAsync());
        Assert.Empty(await s.Repository.GetAllTasksAsync(TaskFilter.All));
        Assert.Equal(0, await s.Repository.CountAllAsync());
        Assert.Empty(await s.Repository.GetPendingSyncAsync());

        await s.Settings.SetAsync(SettingsService.KeyGoogleSub, "cuenta-a");
        Assert.Single(await s.Repository.GetAllTasksAsync(TaskFilter.All));
        Assert.NotEmpty(await s.Repository.GetPendingSyncAsync());
    }

    [Fact]
    public async Task AddTask_GoesOnTop_AndReorderRenumbers()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var list = await repo.CreateListAsync("L");

        var first = await repo.AddTaskAsync(list.Id, " primera ");
        var second = await repo.AddTaskAsync(list.Id, "segunda", inMyDay: true, plannedFor: new DateTime(2026, 10, 1, 15, 0, 0), inProgress: true);
        Assert.Equal("primera", first.Title);
        Assert.True(second.SortOrder < first.SortOrder);
        Assert.Equal(DateTime.Now.Date, second.MyDayOn);
        Assert.Equal(new DateTime(2026, 10, 1), second.PlannedFor);
        Assert.True(second.InProgress);

        Assert.Equal(["segunda", "primera"], (await repo.GetTasksAsync(list.Id)).Select(t => t.Title));

        await repo.ReorderTasksAsync([first.Id, second.Id]);
        Assert.Equal(["primera", "segunda"], (await repo.GetTasksAsync(list.Id)).Select(t => t.Title));

        await repo.ReorderTasksAsync([]);
    }

    [Fact]
    public async Task GetTasks_PinnedFirst_DoneLast_ExcludeDone()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var list = await repo.CreateListAsync("L");

        var done = await repo.AddTaskAsync(list.Id, "hecha");
        done.IsDone = true;
        await repo.UpdateTaskAsync(done);
        await repo.AddTaskAsync(list.Id, "normal");
        var pinned = await repo.AddTaskAsync(list.Id, "anclada");
        await repo.SetPinnedAsync([pinned.Id], true);
        await repo.ReorderTasksAsync([done.Id, pinned.Id]);

        var titles = (await repo.GetTasksAsync(list.Id)).Select(t => t.Title).ToList();
        Assert.Equal("anclada", titles[0]);
        Assert.Equal("hecha", titles[^1]);
        Assert.DoesNotContain("hecha", (await repo.GetTasksAsync(list.Id, includeDone: false)).Select(t => t.Title));
    }

    [Fact]
    public async Task Search_LooksInTitleNotesTagsStepsAndAttachments_IgnoringAccentsCase()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var list = await repo.CreateListAsync("L");

        var byTitle = await repo.AddTaskAsync(list.Id, "Revisar ACCIÓN");
        var byNotes = await repo.AddTaskAsync(list.Id, "a");
        byNotes.Notes = "llamar al fontanero";
        await repo.UpdateTaskAsync(byNotes);
        var byTag = await repo.AddTaskAsync(list.Id, "b");
        await repo.AddTagAsync([byTag.Id], "fontanería");
        var byStep = await repo.AddTaskAsync(list.Id, "c");
        await repo.AddStepsAsync(byStep.Id, ["pedir presupuesto 100%"]);
        var byLink = await repo.AddTaskAsync(list.Id, "d");
        await repo.AddLinkAsync(byLink.Id, "ejemplo.com/factura_2026");
        await repo.AddTaskAsync(list.Id, "nada que ver");

        Assert.Equal([byTitle.Id], (await repo.GetTasksAsync(list.Id, search: "acción")).Select(t => t.Id));
        Assert.Equal([byNotes.Id], (await repo.GetTasksAsync(list.Id, search: " FONTANERO ")).Select(t => t.Id));
        Assert.Equal([byTag.Id], (await repo.GetTasksAsync(list.Id, search: "fontaner\u00eda")).Select(t => t.Id));
        Assert.Equal([byStep.Id], (await repo.GetTasksAsync(list.Id, search: "100%")).Select(t => t.Id));
        Assert.Equal([byLink.Id], (await repo.GetTasksAsync(list.Id, search: "factura_2026")).Select(t => t.Id));

        // «%» y «_» se buscan tal cual, no como comodines de LIKE.
        Assert.Empty(await repo.GetTasksAsync(list.Id, search: "o%o"));
        Assert.Equal(6, (await repo.GetAllTasksAsync(TaskFilter.All, search: "  ")).Count);
    }

    [Fact]
    public async Task Tags_AddRemoveCountDelete_AndNoTagFilter()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var list = await repo.CreateListAsync("L");
        var a = await repo.AddTaskAsync(list.Id, "a");
        var b = await repo.AddTaskAsync(list.Id, "b");
        var c = await repo.AddTaskAsync(list.Id, "c");

        Assert.Equal(0, await repo.AddTagAsync([a.Id], " , "));
        Assert.Equal(2, await repo.AddTagAsync([a.Id, b.Id, a.Id, Guid.NewGuid()], "Casa"));
        Assert.Equal(0, await repo.AddTagAsync([a.Id], "casa"));
        await repo.AddTagAsync([b.Id], "zeta");

        b = (await repo.GetTaskAsync(b.Id))!;
        b.IsDone = true;
        await repo.UpdateTaskAsync(b);

        Assert.Equal((1, 2), await repo.CountTagAsync("casa"));
        Assert.Equal(["Casa", "zeta"], await repo.GetTagsAsync());
        Assert.Equal(["Casa"], await repo.GetTagsAsync(pendingOnly: true));

        Assert.Equal([c.Id], (await repo.GetTasksAsync(list.Id, tag: TaskRepository.NoTag)).Select(t => t.Id));
        Assert.Equal(2, (await repo.GetTasksAsync(list.Id, tag: "CASA")).Count);

        Assert.Equal(1, await repo.RemoveTagAsync([a.Id, c.Id], "casa"));
        Assert.Equal(1, await repo.DeleteTagAsync("Casa"));
        Assert.Equal((0, 0), await repo.CountTagAsync("casa"));
        Assert.Equal(",zeta,", (await repo.GetTaskAsync(b.Id))!.Tags);
    }

    [Fact]
    public async Task Bulk_PinMoveDelete()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var from = await repo.CreateListAsync("A");
        var to = await repo.CreateListAsync("B");
        var t1 = await repo.AddTaskAsync(from.Id, "1");
        var t2 = await repo.AddTaskAsync(from.Id, "2");

        Assert.Equal(2, await repo.SetPinnedAsync([t1.Id, t2.Id], true));
        Assert.Equal(0, await repo.SetPinnedAsync([t1.Id], true));
        Assert.Equal(2, await repo.MoveTasksAsync([t1.Id, t2.Id], to.Id));
        Assert.Equal(0, await repo.MoveTasksAsync([t1.Id], to.Id));
        Assert.Equal(2, await repo.CountInListAsync(to.Id));

        // Mover a «ninguna lista» no se admite.
        await repo.MoveTaskAsync(t1, Guid.Empty);
        Assert.Equal(to.Id, (await repo.GetTaskAsync(t1.Id))!.ListId);

        Assert.Equal(2, await repo.DeleteTasksAsync([t1.Id, t2.Id, Guid.NewGuid()]));
        Assert.Equal(0, await repo.CountAllAsync());
        Assert.Equal(2, (await QueueOf(s)).Count(o => o.Entity == "tasks" && o.Operation == "delete"));
    }

    [Fact]
    public async Task DeleteList_EitherMovesOrDeletesItsTasks()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var keep = await repo.CreateListAsync("Queda");
        var gone = await repo.CreateListAsync("Se va");
        var moved = await repo.AddTaskAsync(gone.Id, "mudada");
        await repo.DeleteListAsync(gone, keep.Id);
        Assert.Equal(keep.Id, (await repo.GetTaskAsync(moved.Id))!.ListId);
        Assert.Null(await repo.GetListAsync(gone.Id));

        var gone2 = await repo.CreateListAsync("Se va con todo");
        var t = await repo.AddTaskAsync(gone2.Id, "borrada");
        await repo.AddStepsAsync(t.Id, ["paso"]);
        await repo.DeleteListAsync(gone2, gone2.Id);
        Assert.Null(await repo.GetTaskAsync(t.Id));
        Assert.Empty(await repo.GetStepsAsync(t.Id));
        Assert.Contains(await QueueOf(s), o => o.Entity == "task_lists" && o.EntityId == gone2.Id.ToString() && o.Operation == "delete");
    }

    [Fact]
    public async Task ApplyRemoteDelete_RemovesRows_WithoutQueueing()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var list = await repo.CreateListAsync("L");
        var t = await repo.AddTaskAsync(list.Id, "t");
        var step = (await repo.AddStepsAsync(t.Id, ["p1", "p2"]))[0];
        var link = await repo.AddLinkAsync(t.Id, "https://a.b");
        var other = await repo.AddTaskAsync(list.Id, "otra");
        var queued = (await QueueOf(s)).Count;

        await repo.ApplyRemoteDeleteAsync("task_steps", step.Id);
        Assert.Single(await repo.GetStepsAsync(t.Id));
        await repo.ApplyRemoteDeleteAsync("task_attachments", link.Id);
        Assert.Empty(await repo.GetAttachmentsAsync(t.Id));
        await repo.ApplyRemoteDeleteAsync("tasks", t.Id);
        Assert.Null(await repo.GetTaskAsync(t.Id));
        await repo.ApplyRemoteDeleteAsync("desconocida", other.Id);
        await repo.ApplyRemoteDeleteAsync("task_lists", list.Id);
        Assert.Null(await repo.GetTaskAsync(other.Id));
        Assert.Null(await repo.GetListAsync(list.Id));

        Assert.Equal(queued, (await QueueOf(s)).Count);
    }

    [Fact]
    public async Task Steps_AddRenameReorderDelete()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var list = await repo.CreateListAsync("L");
        var t = await repo.AddTaskAsync(list.Id, "t");

        Assert.Empty(await repo.AddStepsAsync(t.Id, [" ", ""]));
        var steps = await repo.AddStepsAsync(t.Id, [" uno ", "dos", ""]);
        Assert.Equal(["uno", "dos"], steps.Select(x => x.Title));
        var more = await repo.AddStepsAsync(t.Id, ["tres"], "ai");
        Assert.Equal(2, more[0].SortOrder);
        Assert.Equal("ai", more[0].Source);

        await repo.RenameStepAsync(steps[0], "  ");
        await repo.RenameStepAsync(steps[0], "UNO");
        Assert.Equal("UNO", (await repo.GetStepAsync(steps[0].Id))!.Title);

        await repo.ReorderStepsAsync([more[0].Id, steps[1].Id, steps[0].Id]);
        await repo.ReorderStepsAsync([]);
        Assert.Equal(["tres", "dos", "UNO"], (await repo.GetStepsAsync(t.Id)).Select(x => x.Title));

        steps[1].IsDone = true;
        await repo.UpdateStepAsync(steps[1]);
        var listed = (await repo.GetTasksAsync(list.Id)).Single();
        Assert.Equal(3, listed.StepCount);
        Assert.Equal(1, listed.StepsDone);

        await repo.DeleteStepAsync(steps[1]);
        Assert.Equal(2, (await repo.GetStepsAsync(t.Id)).Count);
    }

    [Fact]
    public async Task Attachments_LinksGetScheme_FilesHaveALimit()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var list = await repo.CreateListAsync("L");
        var t = await repo.AddTaskAsync(list.Id, "t");

        var bare = await repo.AddLinkAsync(t.Id, "  ejemplo.com ");
        Assert.Equal("https://ejemplo.com", bare.Url);
        Assert.Equal("https://ejemplo.com", bare.Name);
        var named = await repo.AddLinkAsync(t.Id, "ftp://x.y/z", " Plano ");
        Assert.Equal("ftp://x.y/z", named.Url);
        Assert.Equal("Plano", named.Name);

        var file = await repo.AddFileAsync(t.Id, @"C:\carpeta\foto.jpg", [1, 2, 3]);
        Assert.Equal("foto.jpg", file.Name);
        Assert.Equal(2, file.SortOrder);
        Assert.Equal(new byte[] { 1, 2, 3 }, (await repo.GetAttachmentAsync(file.Id))!.Data);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.AddFileAsync(t.Id, "enorme.bin", new byte[TaskAttachment.MaxFileBytes + 1]));

        await repo.DeleteAttachmentAsync(named);
        Assert.Equal(2, (await repo.GetAttachmentsAsync(t.Id)).Count);
    }

    [Fact]
    public async Task MyDay_Calendar_Counts()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var list = await repo.CreateListAsync("L");
        var today = DateTime.Now.Date;

        var mine = await repo.AddTaskAsync(list.Id, "hoy", inMyDay: true);
        var planned = await repo.AddTaskAsync(list.Id, "planificada", plannedFor: today.AddDays(2));
        planned.DueAt = today.AddDays(5);
        await repo.UpdateTaskAsync(planned);
        var due = await repo.AddTaskAsync(list.Id, "con plazo");
        due.DueAt = today.AddDays(3);
        due.IsDone = true;
        await repo.UpdateTaskAsync(due);
        await repo.AddTaskAsync(list.Id, "sin fecha");

        Assert.Equal([mine.Id], (await repo.GetMyDayAsync()).Select(t => t.Id));
        Assert.Equal(1, await repo.CountMyDayPendingAsync());

        await repo.ToggleMyDayAsync(mine);
        Assert.Empty(await repo.GetMyDayAsync(today, tag: null));
        await repo.ToggleMyDayAsync(mine);
        Assert.Single(await repo.GetMyDayAsync(today));

        var calendar = await repo.GetCalendarAsync(today, today.AddDays(10));
        Assert.Equal(2, calendar.Count);
        Assert.Equal([planned.Id], calendar[today.AddDays(2)].Select(t => t.Id));
        Assert.False(calendar.ContainsKey(today.AddDays(5))); // se pinta en su dia de inicio, no dos veces
        Assert.Equal([due.Id], calendar[today.AddDays(3)].Select(t => t.Id));
        Assert.Empty(await repo.GetCalendarAsync(today, today.AddDays(10), tag: "inexistente"));

        Assert.Equal(3, await repo.CountPendingAsync());
        Assert.Equal((3, 1), await repo.CountProgressAsync());
        Assert.Equal((3, 1), await repo.CountProgressAsync(list.Id));
    }

    [Fact]
    public async Task Calendar_SortsPendingFirst()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var list = await repo.CreateListAsync("L");
        var day = DateTime.Now.Date.AddDays(1);
        var done = await repo.AddTaskAsync(list.Id, "hecha", plannedFor: day);
        done.IsDone = true;
        await repo.UpdateTaskAsync(done);
        await repo.AddTaskAsync(list.Id, "b", plannedFor: day);
        await repo.AddTaskAsync(list.Id, "a", plannedFor: day);

        var titles = (await repo.GetCalendarAsync(day, day))[day].Select(t => t.Title).ToList();
        Assert.Equal(["a", "b", "hecha"], titles);
    }

    [Fact]
    public async Task AllTasks_PinnedByDueDateThenManualOrder()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var list = await repo.CreateListAsync("L");
        var today = DateTime.Now.Date;

        var late = await repo.AddTaskAsync(list.Id, "anclada tarde");
        late.IsPinned = true; late.DueAt = today.AddDays(9);
        await repo.UpdateTaskAsync(late);
        var soon = await repo.AddTaskAsync(list.Id, "anclada pronto");
        soon.IsPinned = true; soon.DueAt = today.AddDays(1);
        await repo.UpdateTaskAsync(soon);
        var overdue = await repo.AddTaskAsync(list.Id, "caducada");
        overdue.DueAt = today.AddDays(-2);
        await repo.UpdateTaskAsync(overdue);

        Assert.Equal(["anclada pronto", "anclada tarde", "caducada"],
            (await repo.GetAllTasksAsync(TaskFilter.All)).Select(t => t.Title));
        Assert.Equal([overdue.Id], (await repo.GetAllTasksAsync(TaskFilter.Overdue)).Select(t => t.Id));
        Assert.Equal(2, (await repo.GetAllTasksAsync(TaskFilter.Pinned)).Count);
    }

    [Fact]
    public async Task Series_DeleteFuture_DeleteWhole_CountAndBulk()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var list = await repo.CreateListAsync("L");
        var today = DateTime.Now.Date;
        var series = Guid.NewGuid();

        async Task<TaskItem> Add(string title, int offset, bool done = false)
        {
            var t = await repo.AddTaskAsync(list.Id, title, plannedFor: today.AddDays(offset));
            t.SeriesId = series;
            t.IsDone = done;
            await repo.UpdateTaskAsync(t);
            return t;
        }

        var past = await Add("pasada", -3);
        var doneFuture = await Add("hecha", 2, done: true);
        var keep = await Add("la que se queda", 1);
        await Add("futura", 4);
        await Add("otra futura", 7);
        var loose = await repo.AddTaskAsync(list.Id, "suelta");

        Assert.Equal(["pasada", "la que se queda", "hecha", "futura", "otra futura"],
            (await repo.GetSeriesAsync(series)).Select(t => t.Title));
        Assert.Equal(2, await repo.CountInSeriesAsync([past.Id, keep.Id, loose.Id]));

        Assert.Equal(2, await repo.DeleteFutureSeriesAsync(series, keep.Id));
        Assert.Equal([past.Id, keep.Id, doneFuture.Id], (await repo.GetSeriesAsync(series)).Select(t => t.Id));

        // Borrar una seleccion con una vuelta de la serie se lleva la serie entera, una sola vez.
        Assert.Equal(4, await repo.DeleteTasksWithSeriesAsync([past.Id, keep.Id, loose.Id]));
        Assert.Empty(await repo.GetSeriesAsync(series));
        Assert.Equal(0, await repo.CountAllAsync());
    }

    [Fact]
    public async Task Groups_Members_AndDeleteHidesItsLists()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var group = await repo.SaveGroupAsync(new TaskGroup { Name = "Familia", JoinCode = "ABC" });
        Assert.Equal("cuenta-a", group.AccountId);
        var shared = await repo.CreateListAsync("Compra", group.Id);

        await repo.SaveMemberAsync(new GroupMember { Id = $"{group.Id}|u2", GroupId = group.Id, UserId = "u2", DisplayName = "Zoe" });
        await repo.SaveMemberAsync(new GroupMember { Id = $"{group.Id}|u1", GroupId = group.Id, UserId = "u1", DisplayName = "Ana" });

        Assert.Single(await repo.GetGroupsAsync());
        Assert.Equal("Familia", (await repo.GetGroupAsync(group.Id))!.Name);
        Assert.Equal([shared.Id], (await repo.GetGroupListsAsync(group.Id)).Select(l => l.Id));
        Assert.Empty(await repo.GetPrivateListsAsync());
        Assert.Equal(["Ana", "Zoe"], (await repo.GetMembersAsync(group.Id)).Select(m => m.DisplayName));

        await repo.DeleteGroupAsync(group);
        Assert.Empty(await repo.GetGroupsAsync());
        Assert.Empty(await repo.GetGroupListsAsync(group.Id));
    }

    [Fact]
    public async Task Xp_TotalsByAccountAndGroup_RecentAndCompletedCounts()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var g = Guid.NewGuid();
        await repo.AddXpAsync(new XpEvent { UserId = "cuenta-a", Amount = 50 });
        await repo.AddXpAsync(new XpEvent { UserId = "cuenta-a", Amount = 10, GroupId = g });
        await repo.AddXpAsync(new XpEvent { UserId = "otra", Amount = 1000 });

        Assert.Equal(60, await repo.GetTotalXpAsync());
        Assert.Equal(10, await repo.GetTotalXpAsync(g));
        Assert.Equal(2, (await repo.GetRecentXpAsync()).Count);
        Assert.Single(await repo.GetRecentXpAsync(1));

        var list = await repo.CreateListAsync("L");
        var t = await repo.AddTaskAsync(list.Id, "t");
        t.IsDone = true;
        t.DoneAt = DateTime.UtcNow;
        await repo.UpdateTaskAsync(t);
        var old = await repo.AddTaskAsync(list.Id, "vieja");
        old.IsDone = true;
        old.DoneAt = DateTime.UtcNow.AddDays(-200);
        await repo.UpdateTaskAsync(old);

        Assert.Equal([DateTime.Now.Date], await repo.GetActiveDaysAsync());
        Assert.Equal(1, await repo.CountCompletedAsync(DateTime.UtcNow.AddDays(-1)));
    }

    [Fact]
    public async Task ClaimOrphans_AdoptsOnlyUnownedRows()
    {
        await using var s = await TestStore.CreateAsync(account: "");
        var repo = s.Repository;
        var provisional = s.Settings.UserId;

        var orphanList = await repo.CreateListAsync("De antes");
        var orphanTask = await repo.AddTaskAsync(orphanList.Id, "de antes");
        orphanTask.CreatedBy = provisional;
        orphanTask.DoneBy = provisional;
        await repo.UpdateTaskAsync(orphanTask);
        await repo.AddXpAsync(new XpEvent { UserId = provisional, Amount = 50 });
        // Fila con AccountId NULL, como la deja un ALTER TABLE ADD COLUMN sobre una base vieja.
        var nullList = new TaskList { Name = "Nula" };
        await s.Db.Connection.InsertAsync(nullList);
        await s.Db.Connection.ExecuteAsync("UPDATE task_lists SET AccountId = NULL WHERE Id = ?", nullList.Id);

        await s.Settings.SetAsync(SettingsService.KeyGoogleSub, "cuenta-b");
        var other = await repo.CreateListAsync("De B");

        Assert.Equal(0, await repo.ClaimOrphansAsync("", provisional));
        Assert.True(await repo.ClaimOrphansAsync("cuenta-a", provisional) >= 3);
        Assert.Equal(0, await repo.ClaimOrphansAsync("cuenta-a", provisional));

        Assert.Equal("cuenta-a", (await repo.GetListAsync(orphanList.Id))!.AccountId);
        Assert.Equal("cuenta-a", (await repo.GetListAsync(nullList.Id))!.AccountId);
        Assert.Equal("cuenta-b", (await repo.GetListAsync(other.Id))!.AccountId);
        var task = (await repo.GetTaskAsync(orphanTask.Id))!;
        Assert.Equal("cuenta-a", task.CreatedBy);
        Assert.Equal("cuenta-a", task.DoneBy);

        await s.Settings.SetAsync(SettingsService.KeyGoogleSub, "cuenta-a");
        Assert.Equal(50, await repo.GetTotalXpAsync());
        Assert.NotEmpty(await repo.GetPendingSyncAsync());
    }

    [Fact]
    public async Task SyncQueue_EventQueueEverythingAndClear()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var raised = 0;
        repo.LocalChangeQueued += (_, _) => raised++;

        var list = await repo.CreateListAsync("L");
        var t = await repo.AddTaskAsync(list.Id, "t");
        await repo.AddStepsAsync(t.Id, ["p"]);
        await repo.AddLinkAsync(t.Id, "a.b");
        Assert.Equal(4, raised);

        var pending = await repo.GetPendingSyncAsync();
        Assert.Equal(4, pending.Count);
        await repo.ClearSyncAsync(pending);
        await repo.ClearSyncAsync([]);
        Assert.Empty(await repo.GetPendingSyncAsync());

        Assert.Equal(4, await repo.QueueEverythingAsync());
        var ops = await repo.GetPendingSyncAsync();
        Assert.Equal(["task_lists", "tasks", "task_steps", "task_attachments"], ops.Select(o => o.Entity));
        Assert.Equal(2, (await repo.GetPendingSyncAsync(2)).Count);
    }

    [Fact]
    public async Task SaveFromRemote_StampsAccount_AndDoesNotQueue()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var list = new TaskList { Name = "remota", AccountId = "x" };
        var task = new TaskItem { ListId = list.Id, Title = "remota", AccountId = "x" };
        var group = new TaskGroup { Name = "g", AccountId = "x" };

        await repo.SaveFromRemoteAsync(list, isNew: true);
        await repo.SaveFromRemoteAsync(task, isNew: true);
        await repo.SaveFromRemoteAsync(group, isNew: true);
        await repo.SaveFromRemoteAsync(new TaskStep { TaskId = task.Id, Title = "p" }, isNew: true);
        task.Title = "cambiada";
        await repo.SaveFromRemoteAsync(task, isNew: false);

        Assert.Equal("cuenta-a", (await repo.GetListAsync(list.Id))!.AccountId);
        Assert.Equal("cambiada", (await repo.GetTaskAsync(task.Id))!.Title);
        Assert.Single(await repo.GetGroupsAsync());
        Assert.Empty(await repo.GetPendingSyncAsync());
    }
}
