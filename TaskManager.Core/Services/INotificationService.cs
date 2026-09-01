using TaskManager.Core.Models;

namespace TaskManager.Core.Services;

/// <summary>
/// Avisos del sistema: el recordatorio diario de lo que queda pendiente y el de cada tarea con
/// plazo (las repetitivas entran por aqui igual, porque cada vuelta nace con su fecha).
/// </summary>
public interface INotificationService
{
    /// <summary>Si el sistema deja mostrar notificaciones ahora mismo.</summary>
    Task<bool> IsAllowedAsync();

    /// <summary>Pide permiso al usuario. Devuelve si quedo concedido.</summary>
    Task<bool> RequestPermissionAsync();

    /// <summary>
    /// Programa el aviso de una tarea con fecha de finalizacion. Si no tiene fecha, o ya esta
    /// hecha, cancela el que hubiera: una tarea completada no puede seguir avisando.
    /// </summary>
    void ScheduleTaskReminder(TaskItem task);

    void CancelTaskReminder(Guid taskId);

    /// <summary>
    /// Recordatorio diario a la hora indicada con lo que queda pendiente en Mi Dia. Un solo aviso
    /// al dia en vez de uno por tarea: una lista larga no puede convertirse en veinte avisos.
    /// </summary>
    void ScheduleDailySummary(TimeSpan timeOfDay);

    void CancelDailySummary();

    /// <summary>
    /// Avisa <b>ahora</b>, sin programar nada. Es lo que usa la llegada de una tarea creada en otro
    /// dispositivo del mismo usuario: ya ha pasado, no hay hora futura que esperar.
    /// </summary>
    void Notify(string title, string message);
}
