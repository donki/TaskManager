using System.Windows.Threading;
using TaskManager.Core.Data;
using TaskManager.Core.Services;

namespace TaskManager.Desktop.Services;

/// <summary>
/// Recordatorios en Windows: el aviso diario de lo que queda pendiente y el de las tareas con fecha
/// de finalizacion, mostrados como globo del icono de bandeja.
/// </summary>
/// <remarks>
/// En Android esto lo hace <c>AlarmManager</c>, que despierta al sistema aunque la aplicacion este
/// cerrada. Aqui no hace falta tanto: la aplicacion **ya vive en la bandeja**, asi que basta con
/// mirar el reloj cada minuto. Si esta cerrada no hay avisos, que es lo esperable en un programa de
/// escritorio.
/// </remarks>
public sealed class ReminderScheduler : IDisposable
{
    private readonly TaskRepository _repository;
    private readonly SettingsService _settings;
    private readonly TrayIconHost _tray;
    private readonly DispatcherTimer _timer;

    /// <summary>Lo ya avisado hoy, para no repetir el mismo globo cada minuto.</summary>
    private readonly HashSet<Guid> _notifiedTasks = [];

    private DateTime _lastDailySummary = DateTime.MinValue;

    public ReminderScheduler(TaskRepository repository, SettingsService settings, TrayIconHost tray)
    {
        _repository = repository;
        _settings = settings;
        _tray = tray;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start();
    }

    private async Task CheckAsync()
    {
        if (!_settings.NotificationsEnabled)
        {
            return;
        }

        var now = DateTime.Now;

        // Al cambiar el dia se olvida lo avisado: las tareas de hoy vuelven a poder avisar.
        if (_lastDailySummary.Date != now.Date)
        {
            _notifiedTasks.Clear();
        }

        try
        {
            await CheckDailySummaryAsync(now).ConfigureAwait(true);
            await CheckDueTasksAsync(now).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Un aviso fallido no puede tumbar la aplicacion de la bandeja.
            System.Diagnostics.Debug.WriteLine($"Recordatorio fallido: {ex.Message}");
        }
    }

    /// <summary>Un solo globo al dia, a la hora elegida, con lo que queda en Mi Dia.</summary>
    private async Task CheckDailySummaryAsync(DateTime now)
    {
        // Con posposicion, el aviso se repite cada tantos minutos; sin ella, uno al dia.
        var snooze = _settings.SnoozeMinutes;
        var due = snooze > 0
            ? _lastDailySummary.AddMinutes(snooze) <= now
            : _lastDailySummary.Date != now.Date;

        if (!due || now.Hour < _settings.NotifyHour)
        {
            return;
        }

        _lastDailySummary = now;

        var pending = await _repository.CountPendingAsync().ConfigureAwait(true);
        if (pending > 0)
        {
            // Traducido, como todo lo demas: estas dos frases estaban clavadas en español y
            // hablaban de «Mi Día», que ya no existe.
            _tray.Notify(
                Localization.Loc.Get("MenuMyTasks"),
                pending == 1
                    ? Localization.Loc.Get("NotifyOnePending")
                    : Localization.Loc.Format("NotifyManyPending", pending));
        }
    }

    /// <summary>Aviso de las tareas cuyo plazo vence hoy y siguen sin hacer.</summary>
    private async Task CheckDueTasksAsync(DateTime now)
    {
        if (now.Hour < 9)
        {
            return;
        }

        foreach (var task in await _repository.GetAllTasksAsync(TaskManager.Core.Models.TaskFilter.Pending).ConfigureAwait(true))
        {
            if (task.IsDone || task.DueAt?.Date != now.Date || !_notifiedTasks.Add(task.Id))
            {
                continue;
            }

            _tray.Notify(Localization.Loc.Get("DueToday"), task.Title);
        }
    }

    public void Dispose() => _timer.Stop();
}
