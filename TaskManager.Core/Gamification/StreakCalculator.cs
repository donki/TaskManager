namespace TaskManager.Core.Gamification;

/// <summary>
/// Racha "sin castigo" (especificacion 4.B): cuenta dias con al menos una tarea completada y
/// perdona **un** dia suelto, para que tomarse un descanso no borre semanas de constancia.
/// Nunca resta XP.
/// </summary>
public static class StreakCalculator
{
    public static int Current(IEnumerable<DateTime> activeDays, DateTime? today = null)
    {
        var days = activeDays.Select(d => d.Date).Distinct().OrderByDescending(d => d).ToList();
        if (days.Count == 0)
        {
            return 0;
        }

        var reference = (today ?? DateTime.Now).Date;

        // Se admite que hoy todavia no haya nada hecho: la racha sigue viva desde ayer.
        if (days[0] < reference.AddDays(-1))
        {
            var gapFromToday = (reference - days[0]).Days;
            if (gapFromToday > 2)
            {
                return 0;
            }
        }

        var streak = 1;
        var forgiven = false;

        for (var i = 1; i < days.Count; i++)
        {
            var gap = (days[i - 1] - days[i]).Days;
            if (gap == 1)
            {
                streak++;
            }
            else if (gap == 2 && !forgiven)
            {
                // Un unico dia de descanso no rompe la racha, pero tampoco suma.
                forgiven = true;
                streak++;
            }
            else
            {
                break;
            }
        }

        return streak;
    }

    public static int Longest(IEnumerable<DateTime> activeDays)
    {
        var days = activeDays.Select(d => d.Date).Distinct().OrderBy(d => d).ToList();
        if (days.Count == 0)
        {
            return 0;
        }

        var best = 1;
        var run = 1;
        for (var i = 1; i < days.Count; i++)
        {
            run = (days[i] - days[i - 1]).Days == 1 ? run + 1 : 1;
            best = Math.Max(best, run);
        }

        return best;
    }
}
