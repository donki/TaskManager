using TaskManager.Core.Gamification;

namespace TaskManager.Tests;

public class GamificationTests
{
    [Theory]
    [InlineData(-5, 1.0)]
    [InlineData(0, 1.0)]
    [InlineData(1, 1.5)]
    [InlineData(2, 2.0)]
    [InlineData(3, 3.0)]
    [InlineData(40, 3.0)]
    public void Combo_IsClampedToX3(int chain, double expected) => Assert.Equal(expected, XpRules.ComboFor(chain));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 100)]
    [InlineData(3, 300)]
    [InlineData(4, 600)]
    public void XpForLevel(int level, int xp) => Assert.Equal(xp, LevelCurve.XpForLevel(level));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(299, 2)]
    [InlineData(300, 3)]
    public void LevelFor(int xp, int level) => Assert.Equal(level, LevelCurve.LevelFor(xp));

    [Fact]
    public void ProgressAndRemaining()
    {
        Assert.Equal(0.0, LevelCurve.ProgressInLevel(0));
        Assert.Equal(0.5, LevelCurve.ProgressInLevel(200));
        Assert.Equal(100, LevelCurve.XpToNextLevel(200));
        Assert.Equal(100, LevelCurve.XpToNextLevel(0));
    }

    [Fact]
    public void Unlockables_AreOrderedAndReachable()
    {
        Assert.Equal(Unlockables.All.OrderBy(u => u.Level), Unlockables.All);
        Assert.Empty(Unlockables.UnlockedAt(1));
        Assert.Equal(2, Unlockables.UnlockedAt(3).Count());
        Assert.Equal("confetti_stars", Unlockables.NextAfter(3)!.Key);
        Assert.Null(Unlockables.NextAfter(99));
        Assert.Equal(Unlockables.All.Count, Unlockables.All.Select(u => u.Key).Distinct().Count());
    }

    [Fact]
    public void Celebration_IsCombo()
    {
        Assert.False(new Celebration(50, 1.0, 50, 1, false, null).IsCombo);
        Assert.True(new Celebration(75, 1.5, 125, 2, true, null).IsCombo);
    }
}

public class StreakCalculatorTests
{
    private static readonly DateTime Today = new(2026, 9, 29);

    private static DateTime[] Ago(params int[] days) => [.. days.Select(d => Today.AddDays(-d).AddHours(10))];

    [Fact]
    public void Empty_IsZero()
    {
        Assert.Equal(0, StreakCalculator.Current([], Today));
        Assert.Equal(0, StreakCalculator.Longest([]));
    }

    [Fact]
    public void ConsecutiveDays_IncludingToday() => Assert.Equal(3, StreakCalculator.Current(Ago(0, 1, 2), Today));

    [Fact]
    public void NothingToday_StreakStillAliveFromYesterday() => Assert.Equal(2, StreakCalculator.Current(Ago(1, 2), Today));

    [Fact]
    public void OneMissingDay_IsForgivenOnce()
    {
        Assert.Equal(3, StreakCalculator.Current(Ago(0, 2, 3), Today));
        // Dos huecos: solo se perdona el primero.
        Assert.Equal(3, StreakCalculator.Current(Ago(0, 2, 3, 5, 6), Today));
    }

    [Fact]
    public void GapOfTwoDaysFromToday_StillCounts_ButThreeBreaks()
    {
        Assert.Equal(1, StreakCalculator.Current(Ago(2), Today));
        Assert.Equal(0, StreakCalculator.Current(Ago(3, 4), Today));
    }

    [Fact]
    public void DuplicatesOnSameDay_CountOnce() => Assert.Equal(2, StreakCalculator.Current([.. Ago(0, 0, 1), Today.AddHours(22)], Today));

    [Fact]
    public void Longest_FindsBestRun() => Assert.Equal(4, StreakCalculator.Longest(Ago(0, 1, 5, 6, 7, 8, 20)));

    [Fact]
    public void Current_DefaultsToNow() => Assert.Equal(1, StreakCalculator.Current([DateTime.Now]));
}
