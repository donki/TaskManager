using TaskManager.Core.Gamification;
using TaskManager.Core.Models;
using TaskManager.Core.Services;

namespace TaskManager.Tests;

internal sealed class FakeNotifications : INotificationService
{
    public List<Guid> Scheduled { get; } = [];
    public List<Guid> Cancelled { get; } = [];

    public Task<bool> IsAllowedAsync() => Task.FromResult(true);
    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);
    public void ScheduleTaskReminder(TaskItem task) => Scheduled.Add(task.Id);
    public void CancelTaskReminder(Guid taskId) => Cancelled.Add(taskId);
    public void ScheduleDailySummary(TimeSpan timeOfDay) { }
    public void CancelDailySummary() { }
    public void Notify(string title, string message) { }
}

public class TaskServiceTests
{
    [Fact]
    public async Task Initialize_CreatesDefaultList_AndAdoptsOrphans()
    {
        await using var s = await TestStore.CreateAsync(account: "");
        var orphan = await s.Repository.CreateListAsync("De antes");
        await s.Settings.SetAsync(SettingsService.KeyGoogleSub, "cuenta-a");

        var service = s.NewService();
        await service.InitializeAsync();

        Assert.Equal([orphan.Id], (await s.Repository.GetPrivateListsAsync()).Select(l => l.Id));
        Assert.Equal(string.Empty, s.Settings.Get(SettingsService.KeyLocalUserId));
        Assert.Same(s.Repository, service.Repository);

        // Otra cuenta en el mismo aparato: no se lleva la lista de la primera, estrena la suya.
        await s.Settings.SetAsync(SettingsService.KeyGoogleSub, "cuenta-b");
        await service.AdoptAccountAsync("cuenta-b");
        await service.AdoptAccountAsync("");
        var listsB = await s.Repository.GetPrivateListsAsync();
        Assert.Equal("Tareas", Assert.Single(listsB).Name);
    }

    [Fact]
    public async Task Initialize_WithoutAccount_StillHasAList()
    {
        await using var s = await TestStore.CreateAsync(account: "");
        await s.NewService().InitializeAsync();
        Assert.Single(await s.Repository.GetPrivateListsAsync());
    }

    [Fact]
    public async Task Complete_AwardsXp_Combo_Celebrates_AndCancelsReminder()
    {
        await using var s = await TestStore.CreateAsync();
        var notifications = new FakeNotifications();
        var service = s.NewService(notifications);
        await service.InitializeAsync();
        var list = (await s.Repository.GetPrivateListsAsync())[0];
        var celebrated = new List<Celebration>();
        service.Celebrated += (_, c) => celebrated.Add(c);

        var a = await s.Repository.AddTaskAsync(list.Id, "a", inProgress: true);
        var b = await s.Repository.AddTaskAsync(list.Id, "b");

        var first = await service.CompleteTaskAsync(a);
        Assert.NotNull(first);
        Assert.Equal(XpRules.Task, first!.Xp);
        Assert.False(first.IsCombo);
        Assert.False(a.InProgress);
        Assert.Equal("cuenta-a", a.DoneBy);
        Assert.Contains(a.Id, notifications.Cancelled);

        // Segunda dentro de la ventana de combo: x1,5 y sube a nivel 2 (100 XP) con su premio.
        var second = await service.CompleteTaskAsync(b);
        Assert.Equal(75, second!.Xp);
        Assert.True(second.LeveledUp);
        Assert.Equal(2, second.Level);
        Assert.Equal("confetti_classic", second.Unlocked!.Key);
        Assert.Equal(125, await s.Repository.GetTotalXpAsync());
        Assert.Equal(2, celebrated.Count);

        // Ya hecha: no da nada.
        Assert.Null(await service.CompleteTaskAsync(a));
    }

    [Fact]
    public async Task Uncomplete_DoesNotTakeXpAway()
    {
        await using var s = await TestStore.CreateAsync();
        var service = s.NewService();
        await service.InitializeAsync();
        var list = (await s.Repository.GetPrivateListsAsync())[0];
        var t = await s.Repository.AddTaskAsync(list.Id, "t");

        await service.UncompleteTaskAsync(t);
        await service.CompleteTaskAsync(t);
        await service.UncompleteTaskAsync(t);

        Assert.False(t.IsDone);
        Assert.Null(t.DoneAt);
        Assert.Null(t.DoneBy);
        Assert.Equal(XpRules.Task, await s.Repository.GetTotalXpAsync());
    }

    [Fact]
    public async Task CompleteMany_CelebratesOnce_UncompleteMany()
    {
        await using var s = await TestStore.CreateAsync();
        var service = s.NewService();
        await service.InitializeAsync();
        var list = (await s.Repository.GetPrivateListsAsync())[0];
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await s.Repository.AddTaskAsync(list.Id, $"t{i}")).Id);
        }

        var celebrations = 0;
        service.Celebrated += (_, _) => celebrations++;

        var last = await service.CompleteManyAsync([.. ids, ids[0], Guid.NewGuid()]);
        Assert.NotNull(last);
        Assert.Equal(1, celebrations);
        Assert.Equal((0, 3), await s.Repository.CountProgressAsync(list.Id));

        Assert.Null(await service.CompleteManyAsync(ids));
        Assert.Equal(1, celebrations);

        await service.UncompleteManyAsync([ids[0], ids[1], Guid.NewGuid()]);
        Assert.Equal((2, 1), await s.Repository.CountProgressAsync(list.Id));
    }

    [Fact]
    public async Task Complete_RepeatingTask_CreatesNextFutureOccurrence_KeepingPlannedOffset()
    {
        await using var s = await TestStore.CreateAsync();
        var notifications = new FakeNotifications();
        var service = s.NewService(notifications);
        await service.InitializeAsync();
        var list = (await s.Repository.GetPrivateListsAsync())[0];
        var today = DateTime.Now.Date;

        var t = await s.Repository.AddTaskAsync(list.Id, "semanal");
        t.RecurrenceRule = new Recurrence(RecurrenceKind.Weekly, 1).Serialize();
        t.DueAt = today.AddDays(-30);          // olvidada hace un mes
        t.PlannedFor = today.AddDays(-32);     // se planificaba dos dias antes
        t.Tags = ",casa,";
        await s.Repository.UpdateTaskAsync(t);

        await service.CompleteTaskAsync(t);

        var next = (await s.Repository.GetAllTasksAsync(TaskFilter.Pending)).Single();
        Assert.NotEqual(t.Id, next.Id);
        Assert.Equal("semanal", next.Title);
        Assert.Equal(",casa,", next.Tags);
        Assert.True(next.DueAt >= today && next.DueAt < today.AddDays(7));
        Assert.Equal(next.DueAt!.Value.AddDays(-2), next.PlannedFor);
        Assert.Contains(next.Id, notifications.Scheduled);
    }

    [Fact]
    public async Task Complete_RepeatingWithoutDates_OrInSeries()
    {
        await using var s = await TestStore.CreateAsync();
        var service = s.NewService();
        await service.InitializeAsync();
        var list = (await s.Repository.GetPrivateListsAsync())[0];

        var noDates = await s.Repository.AddTaskAsync(list.Id, "diaria");
        noDates.RecurrenceRule = "daily:1";
        await s.Repository.UpdateTaskAsync(noDates);
        await service.CompleteTaskAsync(noDates);
        var next = (await s.Repository.GetAllTasksAsync(TaskFilter.Pending)).Single();
        Assert.Equal(DateTime.Now.Date.AddDays(1), next.DueAt);
        Assert.Null(next.PlannedFor);

        // De una serie: sus vueltas ya estan escritas, no se crea otra.
        next.SeriesId = Guid.NewGuid();
        await s.Repository.UpdateTaskAsync(next);
        await service.CompleteTaskAsync(next);
        Assert.Empty(await s.Repository.GetAllTasksAsync(TaskFilter.Pending));
    }

    [Fact]
    public async Task GenerateSeries_WritesEveryOccurrence_KeepsTime_AndRegenerates()
    {
        await using var s = await TestStore.CreateAsync();
        var notifications = new FakeNotifications();
        var service = s.NewService(notifications);
        await service.InitializeAsync();
        var list = (await s.Repository.GetPrivateListsAsync())[0];
        var start = DateTime.Now.Date.AddDays(1);

        var t = await s.Repository.AddTaskAsync(list.Id, "cada dia");
        t.RecurrenceRule = "daily:1";
        t.PlannedFor = start.AddHours(9);
        t.DueAt = start.AddDays(4).AddHours(18);
        await s.Repository.UpdateTaskAsync(t);

        var result = await service.GenerateSeriesAsync(t);
        Assert.Equal(new TaskService.SeriesResult(5, false), result);
        var series = await s.Repository.GetSeriesAsync(t.SeriesId!.Value);
        Assert.Equal(5, series.Count);
        Assert.All(series, x => Assert.Equal(TimeSpan.FromHours(9), x.PlannedFor!.Value.TimeOfDay));
        Assert.All(series, x => Assert.Equal(TimeSpan.FromHours(18), x.DueAt!.Value.TimeOfDay));
        Assert.Equal(start.AddHours(18), t.DueAt); // la primera vuelta es la propia tarea
        Assert.Equal(4, notifications.Scheduled.Count);

        // Se hace la segunda y se cambia la regla a cada dos dias: lo hecho se queda.
        var second = series[1];
        second.IsDone = true;
        await s.Repository.UpdateTaskAsync(second);
        t.RecurrenceRule = "daily:2";
        t.PlannedFor = start.AddHours(9);
        t.DueAt = start.AddDays(4).AddHours(18);
        var again = await service.GenerateSeriesAsync(t);

        var after = await s.Repository.GetSeriesAsync(t.SeriesId!.Value);
        Assert.Equal(3, again.Created);
        Assert.Equal(4, after.Count); // dias 0, 2, 4 + la hecha del dia 1
        Assert.Contains(after, x => x.Id == second.Id && x.IsDone);
    }

    [Fact]
    public async Task GenerateSeries_NothingToDo_AndTruncated()
    {
        await using var s = await TestStore.CreateAsync();
        var service = s.NewService();
        await service.InitializeAsync();
        var list = (await s.Repository.GetPrivateListsAsync())[0];

        var plain = await s.Repository.AddTaskAsync(list.Id, "sin regla");
        Assert.Equal(TaskService.SeriesResult.Nothing, await service.GenerateSeriesAsync(plain));

        var reversed = await s.Repository.AddTaskAsync(list.Id, "al reves");
        reversed.RecurrenceRule = "daily:1";
        reversed.PlannedFor = DateTime.Now.Date.AddDays(5);
        reversed.DueAt = DateTime.Now.Date;
        Assert.Equal(TaskService.SeriesResult.Nothing, await service.GenerateSeriesAsync(reversed));

        var huge = await s.Repository.AddTaskAsync(list.Id, "eterna");
        huge.RecurrenceRule = "daily:1";
        huge.PlannedFor = DateTime.Now.Date;
        huge.DueAt = DateTime.Now.Date.AddYears(3);
        var result = await service.GenerateSeriesAsync(huge);
        Assert.True(result.Truncated);
        Assert.Equal(Recurrence.MaxOccurrences, result.Created);
    }

    [Fact]
    public async Task InProgress_TogglesAndReopensDone()
    {
        await using var s = await TestStore.CreateAsync();
        var service = s.NewService();
        await service.InitializeAsync();
        var list = (await s.Repository.GetPrivateListsAsync())[0];
        var t = await s.Repository.AddTaskAsync(list.Id, "t");

        await service.SetInProgressAsync(t, false); // nada que cambiar
        await service.SetInProgressAsync(t, true);
        Assert.True((await s.Repository.GetTaskAsync(t.Id))!.InProgress);

        await service.CompleteTaskAsync(t);
        Assert.False(t.InProgress);
        await service.SetInProgressAsync(t, true);
        Assert.True(t.InProgress);
        Assert.False(t.IsDone);
        Assert.Null(t.DoneBy);
    }

    [Fact]
    public async Task ToggleStep_AwardsStepXp_AndLastStepCompletesTask()
    {
        await using var s = await TestStore.CreateAsync();
        var service = s.NewService();
        await service.InitializeAsync();
        var group = await s.Repository.SaveGroupAsync(new TaskGroup { Name = "g" });
        var list = await s.Repository.CreateListAsync("del grupo", group.Id);
        var t = await s.Repository.AddTaskAsync(list.Id, "t");
        var steps = await s.Repository.AddStepsAsync(t.Id, ["1", "2"]);

        var c1 = await service.ToggleStepAsync(steps[0]);
        Assert.Equal(XpRules.Step, c1!.Xp);
        Assert.False((await s.Repository.GetTaskAsync(t.Id))!.IsDone);

        Assert.Null(await service.ToggleStepAsync(steps[0])); // desmarcar
        await service.ToggleStepAsync(steps[0]);
        var last = await service.ToggleStepAsync(steps[1]);
        Assert.Equal(XpRules.Task, last!.Xp);
        Assert.True((await s.Repository.GetTaskAsync(t.Id))!.IsDone);

        // El XP de una tarea de un grupo cuenta para ese grupo.
        Assert.Equal(3 * XpRules.Step + XpRules.Task, await s.Repository.GetTotalXpAsync(group.Id));
    }
}
