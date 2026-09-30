using TaskManager.Core.Models;

namespace TaskManager.Tests;

public class RecurrenceTests
{
    private static Recurrence Weekly(int interval, params DayOfWeek[] days) =>
        new(RecurrenceKind.Weekly, interval, Recurrence.MaskOf(days));

    [Fact]
    public void None_DoesNotRepeat_AndSerializesEmpty()
    {
        Assert.False(Recurrence.None.Repeats);
        Assert.Equal(string.Empty, Recurrence.None.Serialize());
        Assert.False(new Recurrence(RecurrenceKind.Daily, 0).Repeats);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("none:1")]
    [InlineData("cadahora:2")]
    public void Parse_Garbage_IsNone(string? value) => Assert.Equal(Recurrence.None, Recurrence.Parse(value));

    [Theory]
    [InlineData("daily:1")]
    [InlineData("weekly:2")]
    [InlineData("weekly:1:62")]
    [InlineData("monthly:3")]
    [InlineData("monthly:1:31")]
    [InlineData("yearly:1")]
    [InlineData("yearly:1:9:15")]
    [InlineData("yearly:2:2:0")]
    public void Serialize_RoundTrips(string stored) =>
        Assert.Equal(stored, Recurrence.Parse(stored).Serialize());

    [Fact]
    public void Parse_BadInterval_FallsBackToOne_AndClampsExtras()
    {
        Assert.Equal(1, Recurrence.Parse("daily:0").Interval);
        Assert.Equal(1, Recurrence.Parse("daily:x").Interval);
        Assert.Equal(1, Recurrence.Parse("DAILY").Interval);
        Assert.Equal(31, Recurrence.Parse("monthly:1:99").MonthDay);

        var yearly = Recurrence.Parse("yearly:1:40:77");
        Assert.Equal(12, yearly.Month);
        Assert.Equal(31, yearly.MonthDay);
    }

    [Fact]
    public void Flags_DependOnKind()
    {
        Assert.True(new Recurrence(RecurrenceKind.Daily, 1).UsesDays);
        Assert.False(new Recurrence(RecurrenceKind.Monthly, 1).UsesDays);
        Assert.True(new Recurrence(RecurrenceKind.Monthly, 1).UsesMonthDay);
        Assert.True(new Recurrence(RecurrenceKind.Yearly, 1).UsesMonth);
        Assert.False(new Recurrence(RecurrenceKind.Monthly, 1).UsesMonth);
    }

    [Fact]
    public void MaskOf_And_Includes()
    {
        var mask = Recurrence.MaskOf([DayOfWeek.Sunday, DayOfWeek.Saturday]);
        Assert.Equal(0b1000001, mask);

        var r = new Recurrence(RecurrenceKind.Daily, 1, mask);
        Assert.True(r.Includes(DayOfWeek.Sunday));
        Assert.False(r.Includes(DayOfWeek.Monday));
        Assert.True(new Recurrence(RecurrenceKind.Daily, 1).Includes(DayOfWeek.Monday));
    }

    [Fact]
    public void Next_SimpleKinds()
    {
        var from = new DateTime(2026, 1, 15, 9, 30, 0);
        Assert.Equal(from.AddDays(3), new Recurrence(RecurrenceKind.Daily, 3).Next(from));
        Assert.Equal(from.AddDays(14), new Recurrence(RecurrenceKind.Weekly, 2).Next(from));
        Assert.Equal(from.AddMonths(1), new Recurrence(RecurrenceKind.Monthly, 1).Next(from));
        Assert.Equal(from.AddYears(1), new Recurrence(RecurrenceKind.Yearly, 1).Next(from));
        Assert.Equal(from, Recurrence.None.Next(from));
    }

    [Fact]
    public void Next_Monthly_On31_StaysAtEndOfShortMonth()
    {
        var r = new Recurrence(RecurrenceKind.Monthly, 1, MonthDay: 31);
        Assert.Equal(new DateTime(2026, 2, 28, 8, 0, 0), r.Next(new DateTime(2026, 1, 31, 8, 0, 0)));
        Assert.Equal(new DateTime(2028, 2, 29), r.Next(new DateTime(2028, 1, 31)));
        Assert.Equal(new DateTime(2026, 3, 31), r.Next(new DateTime(2026, 2, 28)));
    }

    [Fact]
    public void Next_Yearly_WithFixedDate_AndLeapDay()
    {
        var r = new Recurrence(RecurrenceKind.Yearly, 1, MonthDay: 29, Month: 2);
        Assert.Equal(new DateTime(2027, 2, 28), r.Next(new DateTime(2026, 5, 1)));
        Assert.Equal(new DateTime(2028, 2, 29), r.Next(new DateTime(2027, 5, 1)));

        var onlyMonth = new Recurrence(RecurrenceKind.Yearly, 1, Month: 9);
        Assert.Equal(new DateTime(2027, 9, 10), onlyMonth.Next(new DateTime(2026, 3, 10)));
    }

    [Fact]
    public void Next_WithDays_JumpsToFirstChosenDay()
    {
        // Jueves 1 de octubre de 2026 + 1 dia = viernes; solo lunes => lunes 5.
        var r = new Recurrence(RecurrenceKind.Daily, 1, Recurrence.MaskOf([DayOfWeek.Monday]));
        Assert.Equal(new DateTime(2026, 10, 5), r.Next(new DateTime(2026, 10, 1)));
    }

    [Fact]
    public void Occurrences_Daily_Inclusive()
    {
        var days = new Recurrence(RecurrenceKind.Daily, 2).Occurrences(new DateTime(2026, 1, 1), new DateTime(2026, 1, 7)).ToList();
        Assert.Equal([new(2026, 1, 1), new(2026, 1, 3), new(2026, 1, 5), new DateTime(2026, 1, 7)], days);
    }

    [Fact]
    public void Occurrences_Daily_WithDays_SkipsUnchosen()
    {
        var r = new Recurrence(RecurrenceKind.Daily, 1, Recurrence.MaskOf([DayOfWeek.Saturday, DayOfWeek.Sunday]));
        var days = r.Occurrences(new DateTime(2026, 10, 1), new DateTime(2026, 10, 11)).ToList();
        Assert.Equal(4, days.Count);
        Assert.All(days, d => Assert.True(d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday));
    }

    [Fact]
    public void Occurrences_EmptyWhenReversedOrNotRepeating()
    {
        Assert.Empty(new Recurrence(RecurrenceKind.Daily, 1).Occurrences(new DateTime(2026, 2, 1), new DateTime(2026, 1, 1)));
        Assert.Empty(Recurrence.None.Occurrences(new DateTime(2026, 1, 1), new DateTime(2026, 2, 1)));
    }

    [Fact]
    public void Occurrences_Biweekly_Tuesdays_StartingOnThursday_DoesNotSkipFirstWeek()
    {
        // El caso que se documenta en el codigo: a partir del jueves 1 de octubre de 2026.
        var days = Weekly(2, DayOfWeek.Tuesday)
            .Occurrences(new DateTime(2026, 10, 1), new DateTime(2026, 11, 30)).ToList();
        Assert.Equal([new(2026, 10, 6), new(2026, 10, 20), new(2026, 11, 3), new(2026, 11, 17)], days);
    }

    [Fact]
    public void Occurrences_Weekly_SeveralDays_AllInEachWeek()
    {
        var days = Weekly(1, DayOfWeek.Monday, DayOfWeek.Wednesday)
            .Occurrences(new DateTime(2026, 10, 5), new DateTime(2026, 10, 18)).ToList();
        Assert.Equal([new(2026, 10, 5), new(2026, 10, 7), new(2026, 10, 12), new DateTime(2026, 10, 14)], days);
    }

    [Fact]
    public void Occurrences_Weekly_NoDays_UsesStartWeekday()
    {
        var days = new Recurrence(RecurrenceKind.Weekly, 1)
            .Occurrences(new DateTime(2026, 10, 1), new DateTime(2026, 10, 22)).ToList();
        Assert.Equal(4, days.Count);
        Assert.All(days, d => Assert.Equal(DayOfWeek.Thursday, d.DayOfWeek));
    }

    [Fact]
    public void Occurrences_Monthly_FixedDay_StartsAtNextMatch()
    {
        var r = new Recurrence(RecurrenceKind.Monthly, 1, MonthDay: 15);
        var days = r.Occurrences(new DateTime(2026, 1, 20), new DateTime(2026, 4, 30)).ToList();
        Assert.Equal([new(2026, 2, 15), new(2026, 3, 15), new DateTime(2026, 4, 15)], days);

        var sameMonth = r.Occurrences(new DateTime(2026, 1, 10), new DateTime(2026, 1, 31)).ToList();
        Assert.Equal([new DateTime(2026, 1, 15)], sameMonth);
    }

    [Fact]
    public void Occurrences_Yearly_FixedDate_StartsNextYearWhenPassed()
    {
        var r = new Recurrence(RecurrenceKind.Yearly, 1, MonthDay: 15, Month: 9);
        var days = r.Occurrences(new DateTime(2026, 10, 1), new DateTime(2028, 12, 31)).ToList();
        Assert.Equal([new(2027, 9, 15), new DateTime(2028, 9, 15)], days);

        var thisYear = r.Occurrences(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)).ToList();
        Assert.Equal([new DateTime(2026, 9, 15)], thisYear);
    }

    [Fact]
    public void Occurrences_CappedAtMax()
    {
        var daily = new Recurrence(RecurrenceKind.Daily, 1).Occurrences(new DateTime(2020, 1, 1), new DateTime(2030, 1, 1));
        Assert.Equal(Recurrence.MaxOccurrences, daily.Count());

        var weekly = Weekly(1, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday)
            .Occurrences(new DateTime(2020, 1, 1), new DateTime(2030, 1, 1));
        Assert.Equal(Recurrence.MaxOccurrences, weekly.Count());
    }

    [Fact]
    public async Task Describe_InBothLanguages()
    {
        await using var es = await TestStore.CreateAsync(language: "es");
        await using var en = await TestStore.CreateAsync(language: "en");

        Assert.Equal("No se repite", Recurrence.None.Describe(es.Texts));
        Assert.Equal("Cada día", new Recurrence(RecurrenceKind.Daily, 1).Describe(es.Texts));
        Assert.Equal("Every 3 days", new Recurrence(RecurrenceKind.Daily, 3).Describe(en.Texts));
        Assert.Equal("Cada 2 semanas · L X", Weekly(2, DayOfWeek.Monday, DayOfWeek.Wednesday).Describe(es.Texts));
        Assert.EndsWith("· day 31", new Recurrence(RecurrenceKind.Monthly, 1, MonthDay: 31).Describe(en.Texts));

        foreach (var kind in new[] { RecurrenceKind.Weekly, RecurrenceKind.Monthly, RecurrenceKind.Yearly })
        {
            Assert.NotEqual(new Recurrence(kind, 1).Describe(en.Texts), new Recurrence(kind, 2).Describe(en.Texts));
            Assert.DoesNotContain("Repeat", new Recurrence(kind, 2).Describe(en.Texts));
        }

        var yearly = new Recurrence(RecurrenceKind.Yearly, 1, MonthDay: 15, Month: 9).Describe(en.Texts);
        Assert.Contains("15", yearly);
        Assert.Contains(System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.MonthNames[8], yearly);

        var monthOnly = new Recurrence(RecurrenceKind.Yearly, 1, Month: 9).Describe(en.Texts);
        Assert.DoesNotContain("15", monthOnly);
    }
}

public class TaskTagsTests
{
    [Fact]
    public void Split_HandlesEmptyDuplicatesAndSpaces()
    {
        Assert.Empty(TaskTags.Split(null));
        Assert.Empty(TaskTags.Split("  "));
        Assert.Equal(["casa", "urgente"], TaskTags.Split(",casa, urgente ,Casa,,"));
    }

    [Fact]
    public void Join_WrapsWithCommas_AndDropsEmpty()
    {
        Assert.Equal(",casa,urgente,", TaskTags.Join(["casa", " urgente ", ",", "CASA"]));
        Assert.Equal(string.Empty, TaskTags.Join([" ", ","]));
    }

    [Fact]
    public void FromInput_ToInput_Has()
    {
        var stored = TaskTags.FromInput("casa; trabajo, casa");
        Assert.Equal(",casa,trabajo,", stored);
        Assert.Equal("casa, trabajo", TaskTags.ToInput(stored));
        Assert.Equal(string.Empty, TaskTags.FromInput(null));
        Assert.True(TaskTags.Has(stored, "TRABAJO"));
        Assert.False(TaskTags.Has(stored, "ocio"));
    }
}

public class TaskFilterTests
{
    private static readonly DateTime Today = new(2026, 9, 29);

    [Fact]
    public void All_ContainsEveryFilterOnce_AndDefaultIsPending()
    {
        Assert.Equal(Enum.GetValues<TaskFilter>().Length, TaskFilters.All.Length);
        Assert.Equal(TaskFilters.All.Length, TaskFilters.All.Distinct().Count());
        Assert.Equal(TaskFilter.Pending, TaskFilters.Default);
        Assert.Equal(TaskFilter.All, TaskFilters.All[0]);
    }

    [Fact]
    public void KeyOf_IsDistinctPerFilter() =>
        Assert.Equal(TaskFilters.All.Length, TaskFilters.All.Select(TaskFilters.KeyOf).Distinct().Count());

    [Theory]
    [InlineData(TaskFilter.Pending, false, false, null, null, true)]
    [InlineData(TaskFilter.Pending, true, false, null, null, false)]
    [InlineData(TaskFilter.Pinned, false, true, null, null, true)]
    [InlineData(TaskFilter.Pinned, true, true, null, null, false)]
    [InlineData(TaskFilter.Done, true, false, null, null, true)]
    [InlineData(TaskFilter.All, true, false, null, null, true)]
    [InlineData(TaskFilter.Overdue, false, false, null, -1, true)]
    [InlineData(TaskFilter.Overdue, true, false, null, -1, false)]
    [InlineData(TaskFilter.Overdue, false, false, null, 0, false)]
    [InlineData(TaskFilter.Overdue, false, false, null, null, false)]
    [InlineData(TaskFilter.StartedBefore, false, false, -1, null, true)]
    [InlineData(TaskFilter.StartedBefore, false, false, 0, null, false)]
    [InlineData(TaskFilter.StartsFromToday, false, false, 0, null, true)]
    [InlineData(TaskFilter.StartsFromToday, false, false, null, null, false)]
    [InlineData(TaskFilter.DueBefore, true, false, null, -3, true)]
    [InlineData(TaskFilter.DueBefore, true, false, null, 0, false)]
    [InlineData(TaskFilter.DueFromToday, false, false, null, 0, true)]
    [InlineData(TaskFilter.DueFromToday, false, false, null, -1, false)]
    public void Matches(TaskFilter filter, bool done, bool pinned, int? plannedOffset, int? dueOffset, bool expected)
    {
        var task = new TaskItem
        {
            IsDone = done,
            IsPinned = pinned,
            // Con hora: el filtro compara solo la parte de fecha.
            PlannedFor = plannedOffset is { } p ? Today.AddDays(p).AddHours(23) : null,
            DueAt = dueOffset is { } d ? Today.AddDays(d).AddHours(23) : null,
        };
        Assert.Equal(expected, TaskFilters.Matches(task, filter, Today));
    }
}
