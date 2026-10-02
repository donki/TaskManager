using TaskManager.Core.Models;
using TaskManager.Core.Services;

namespace TaskManager.Mobile.Services;

/// <summary>Lo que lleva dentro una alarma: el aviso de una tarea o el resumen diario.</summary>
public sealed record ReminderAlarm(string? Title = null, Guid? TaskId = null, bool DailySummary = false);

/// <summary>
/// Lo nativo de los avisos: programar y cancelar una alarma, enseñar una notificacion y el permiso.
/// En Android es <c>AlarmManager</c> y <c>NotificationManagerCompat</c>
/// (<c>Platforms/Android/NotificationService.cs</c>); lo que decide <i>cuando</i> avisar vive en
/// <see cref="ReminderScheduler"/>, que no depende del sistema y se prueba fuera del movil.
/// </summary>
public interface IReminderPlatform
{
    Task<bool> IsAllowedAsync();

    Task<bool> RequestPermissionAsync();

    /// <summary>Alarma inexacta (ventana de 15 minutos) en <paramref name="moment"/>, hora local.</summary>
    void Schedule(int requestCode, ReminderAlarm alarm, DateTime moment);

    void Cancel(int requestCode);

    /// <summary>Notificacion inmediata.</summary>
    void Show(int id, string title, string text);
}

/// <inheritdoc cref="INotificationService"/>
/// <remarks>
/// Los avisos son <b>inexactos</b> a proposito: la alarma exacta es un permiso restringido en
/// Android 12+ que Google reserva para alarmas y temporizadores de verdad, y un recordatorio de
/// tareas no lo es. Un margen de unos minutos no le importa a nadie y evita pedir un permiso que no
/// toca.
/// </remarks>
public sealed class ReminderScheduler : INotificationService
{
    /// <summary>Id fijo del recordatorio diario; los de tarea usan el hash de su identificador.</summary>
    public const int DailyRequestCode = 1;

    private readonly IReminderPlatform _platform;
    private readonly Func<DateTime> _now;

    public ReminderScheduler(IReminderPlatform platform, Func<DateTime>? now = null)
    {
        _platform = platform;
        _now = now ?? (() => DateTime.Now);
    }

    public Task<bool> IsAllowedAsync() => _platform.IsAllowedAsync();

    public Task<bool> RequestPermissionAsync() => _platform.RequestPermissionAsync();

    public void ScheduleTaskReminder(TaskItem task)
    {
        var code = RequestCodeFor(task.Id);

        if (TaskMoment(task, _now()) is not { } moment)
        {
            _platform.Cancel(code);
            return;
        }

        _platform.Schedule(code, new ReminderAlarm(task.Title, task.Id), moment);
    }

    public void CancelTaskReminder(Guid taskId) => _platform.Cancel(RequestCodeFor(taskId));

    /// <summary>
    /// Aviso inmediato. Se reutiliza el canal de los recordatorios a proposito: para quien recibe,
    /// «una tarea nueva» y «una tarea que vence» son lo mismo —cosas que hacer— y partirlo en dos
    /// canales solo obligaria a silenciar dos veces.
    /// </summary>
    public void Notify(string title, string message) => _platform.Show(message.GetHashCode(), title, message);

    public void ScheduleDailySummary(TimeSpan timeOfDay) =>
        _platform.Schedule(DailyRequestCode, new ReminderAlarm(DailySummary: true), NextDaily(timeOfDay, _now()));

    public void CancelDailySummary() => _platform.Cancel(DailyRequestCode);

    /// <summary>
    /// Cuando avisar de una tarea: a las 9:00 del dia del plazo, no a medianoche, que es cuando
    /// nadie lo lee. Null si no hay nada que avisar: sin plazo, ya hecha o borrada, o con ese
    /// momento ya pasado (entonces lo que hubiera programado sobra).
    /// </summary>
    public static DateTime? TaskMoment(TaskItem task, DateTime now)
    {
        if (task.IsDone || task.Deleted || task.DueAt is not { } due)
        {
            return null;
        }

        var moment = due.Date.AddHours(9);
        return moment <= now ? null : moment;
    }

    /// <summary>La proxima vez que toca el resumen diario: hoy a esa hora, o mañana si ya paso.</summary>
    public static DateTime NextDaily(TimeSpan timeOfDay, DateTime now)
    {
        var next = now.Date.Add(timeOfDay);
        return next <= now ? next.AddDays(1) : next;
    }

    /// <summary>Codigo estable por tarea: el mismo identificador siempre reprograma su aviso.</summary>
    public static int RequestCodeFor(Guid id) => Math.Abs(id.GetHashCode()) % 1_000_000 + 10;
}
