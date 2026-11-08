using TaskManager.Core.Models;
using TaskManager.Core.Services;

namespace TaskManager.Tests;

/// <summary>
/// El resumen de solo lectura de una tarea y el tema elegido por dispositivo (2026-11-08).
/// </summary>
public class SummaryAndThemeTests
{
    [Fact]
    public async Task Theme_DefaultsToSystem_AndRemembersTheChoice()
    {
        await using var s = await TestStore.CreateAsync();

        Assert.Equal(ThemeChoice.System, s.Settings.Theme);

        await s.Settings.SetThemeAsync(ThemeChoice.Dark);
        Assert.Equal(ThemeChoice.Dark, s.Settings.Theme);
        Assert.Equal("dark", s.Settings.Get(SettingsService.KeyTheme));

        await s.Settings.SetThemeAsync(ThemeChoice.Light);
        Assert.Equal(ThemeChoice.Light, s.Settings.Theme);

        // Volver a «sistema» deja la clave vacia, igual que si nunca se hubiera tocado.
        await s.Settings.SetThemeAsync(ThemeChoice.System);
        Assert.Equal(ThemeChoice.System, s.Settings.Theme);
        Assert.Equal(string.Empty, s.Settings.Get(SettingsService.KeyTheme));
    }

    [Fact]
    public async Task Theme_UnknownValue_FallsBackToSystem()
    {
        await using var s = await TestStore.CreateAsync();
        await s.Settings.SetAsync(SettingsService.KeyTheme, "purple");
        Assert.Equal(ThemeChoice.System, s.Settings.Theme);
    }

    [Fact]
    public async Task Summary_NewTask_ShowsOnlyStatusAndList()
    {
        await using var s = await TestStore.CreateAsync(language: "es");
        var task = new TaskItem { Title = "Comprar pan" };

        var summary = TaskSummary.Build(task, "Casa", [], s.Texts);

        Assert.Equal("Comprar pan", summary.Title);
        Assert.Collection(summary.Lines,
            l => Assert.Equal(new SummaryLine("Estado", "Pendiente"), l),
            l => Assert.Equal(new SummaryLine("Lista", "Casa"), l));
        Assert.Empty(summary.Steps);
        Assert.Equal(string.Empty, summary.Notes);
        Assert.True(summary.IsBare);
    }

    [Fact]
    public async Task Summary_WithEverything_ListsEachFieldInOrder()
    {
        await using var s = await TestStore.CreateAsync(language: "en");
        var today = new DateTime(2026, 11, 8);
        var task = new TaskItem
        {
            Title = "Report",
            InProgress = true,
            PlannedFor = today,
            DueAt = today.AddDays(3),
            Tags = ",work,urgent,",
            RecurrenceRule = new Recurrence(RecurrenceKind.Weekly, 1).Serialize(),
            Notes = "  Send to the team  ",
        };
        TaskStep[] steps =
        [
            new() { Title = "Draft", IsDone = true, SortOrder = 0 },
            new() { Title = "Review", SortOrder = 1 },
            new() { Title = "Gone", Deleted = true, SortOrder = 2 },
        ];

        var summary = TaskSummary.Build(task, "Office", steps, s.Texts, today);

        Assert.Equal(
            ["Status", "List", "Planned for", "Due date", "Repeat", "Tags", "Steps"],
            summary.Lines.Select(l => l.Label));
        Assert.Equal("In progress", summary.Lines[0].Value);
        Assert.Equal("today", summary.Lines[2].Value);
        Assert.Equal("Every week", summary.Lines[4].Value);
        Assert.Equal("#work  #urgent", summary.Lines[5].Value);
        Assert.Equal("1/2 steps", summary.Lines[6].Value);

        // Los pasos borrados no se cuentan ni se enseñan.
        Assert.Equal(["Draft", "Review"], summary.Steps.Select(st => st.Title));
        Assert.Equal("Send to the team", summary.Notes);
        Assert.False(summary.IsBare);
    }

    [Fact]
    public async Task Summary_OnlyADueDate_IsNotBare()
    {
        await using var s = await TestStore.CreateAsync(language: "es");
        var task = new TaskItem { Title = "x", DueAt = new DateTime(2027, 1, 5) };

        var summary = TaskSummary.Build(task, "Casa", [], s.Texts, new DateTime(2026, 11, 8));

        Assert.False(summary.IsBare);
        Assert.Equal(new DateTime(2027, 1, 5).ToString("d MMM yyyy"), summary.Lines[^1].Value);
    }

    [Fact]
    public async Task Summary_DoneTask_SaysDone()
    {
        await using var s = await TestStore.CreateAsync(language: "es");
        var summary = TaskSummary.Build(new TaskItem { Title = "x", IsDone = true }, null, [], s.Texts);

        Assert.Equal("Hecha", summary.Lines[0].Value);
        Assert.Single(summary.Lines);
        Assert.True(summary.IsBare);
    }
}
