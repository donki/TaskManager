using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// "El Tablon del Gremio" (especificacion 3 y 4.B): nivel, racha, estadisticas y lo desbloqueado.
/// Todo sale de TaskService, que es quien calcula igual en movil y en escritorio.
/// </summary>
public partial class BoardPage : ContentPage
{
    private readonly TaskService _tasks;

    public BoardPage()
        : this(ServiceHelper.GetRequiredService<TaskService>())
    {
    }

    public BoardPage(TaskService tasks)
    {
        InitializeComponent();
        _tasks = tasks;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _tasks.InitializeAsync();

        var board = await _tasks.GetBoardAsync();

        LevelLabel.Text = Localization.Loc.Instance.Format("Level", board.Level);
        XpLabel.Text = Localization.Loc.Instance.Format("XpTotal", board.TotalXp);
        LevelProgress.Progress = board.ProgressInLevel;
        NextLevelLabel.Text = Localization.Loc.Instance.Format("ToNextLevel", board.XpToNextLevel, board.Level + 1);

        StreakLabel.Text = board.CurrentStreak switch
        {
            0 => Localization.Loc.Instance["NoStreak"],
            1 => Localization.Loc.Instance["StreakOne"],
            _ => Localization.Loc.Instance.Format("StreakMany", board.CurrentStreak),
        };
        StreakHint.Text = Localization.Loc.Instance["StreakHint"];

        TodayLabel.Text = Localization.Loc.Instance.Format("Today", board.CompletedToday);
        WeekLabel.Text = Localization.Loc.Instance.Format("LastWeek", board.CompletedThisWeek);
        LongestLabel.Text = Localization.Loc.Instance.Format("LongestStreak", board.LongestStreak);

        UnlockedList.Clear();
        foreach (var unlockable in board.Unlocked)
        {
            UnlockedList.Add(new Label { Text = $"• {unlockable.Name}" });
        }

        if (board.Unlocked.Count == 0)
        {
            UnlockedList.Add(new Label { Text = Localization.Loc.Instance["NothingUnlocked"] });
        }

        NextUnlockLabel.Text = board.NextUnlock is { } next
            ? Localization.Loc.Instance.Format("NextUnlock", next.Name, next.Level)
            : Localization.Loc.Instance["AllUnlocked"];
    }
}
