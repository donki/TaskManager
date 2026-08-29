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

        LevelLabel.Text = $"Nivel {board.Level}";
        XpLabel.Text = $"{board.TotalXp} XP";
        LevelProgress.Progress = board.ProgressInLevel;
        NextLevelLabel.Text = $"Faltan {board.XpToNextLevel} XP para el nivel {board.Level + 1}";

        StreakLabel.Text = board.CurrentStreak switch
        {
            0 => "Sin racha todavía",
            1 => "1 día de racha",
            _ => $"{board.CurrentStreak} días de racha",
        };
        StreakHint.Text = "Un día de descanso no rompe la racha.";

        TodayLabel.Text = $"Hoy: {board.CompletedToday} tareas completadas";
        WeekLabel.Text = $"Últimos 7 días: {board.CompletedThisWeek}";
        LongestLabel.Text = $"Racha más larga: {board.LongestStreak} días";

        UnlockedList.Clear();
        foreach (var unlockable in board.Unlocked)
        {
            UnlockedList.Add(new Label { Text = $"• {unlockable.Name}" });
        }

        if (board.Unlocked.Count == 0)
        {
            UnlockedList.Add(new Label { Text = "Todavía nada. Al nivel 2 llega el primero." });
        }

        NextUnlockLabel.Text = board.NextUnlock is { } next
            ? $"Siguiente: {next.Name} en el nivel {next.Level}"
            : "Todo desbloqueado.";
    }
}
