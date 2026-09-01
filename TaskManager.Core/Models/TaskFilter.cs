namespace TaskManager.Core.Models;

/// <summary>
/// Los filtros de la pantalla «Mis tareas».
/// </summary>
/// <remarks>
/// <para>Vive en el nucleo, y no en cada aplicacion, porque la lista de filtros y lo que significa
/// cada uno tiene que ser <b>la misma</b> en Windows y en Android. Repetirla en los dos sitios es
/// garantizar que dentro de un mes «caducadas» quiera decir cosas distintas segun el aparato.</para>
///
/// <para>Dos fechas distintas, que se confunden con facilidad: la de <i>inicio</i>
/// (<see cref="TaskItem.PlannedFor"/>) es cuando toca ponerse, y la de <i>caducidad</i>
/// (<see cref="TaskItem.DueAt"/>) es cuando deja de valer.</para>
/// </remarks>
public enum TaskFilter
{
    /// <summary>Lo que queda por hacer. Es el filtro de partida: es a lo que se viene.</summary>
    Pending,

    Done,

    All,

    /// <summary>Caducadas: con fecha de caducidad pasada y todavia sin hacer.</summary>
    Overdue,

    /// <summary>Fecha de inicio anterior a hoy: ya tendrian que haber empezado.</summary>
    StartedBefore,

    /// <summary>Fecha de inicio de hoy en adelante: aun no toca.</summary>
    StartsFromToday,

    /// <summary>Fecha de caducidad anterior a hoy.</summary>
    DueBefore,

    /// <summary>Fecha de caducidad de hoy en adelante.</summary>
    DueFromToday,
}

public static class TaskFilters
{
    /// <summary>
    /// El que sale puesto al abrir. <b>No es el primero de la fila</b>: «Todas» va delante porque es
    /// el que quita el filtro, pero a lo que se viene es a lo que queda por hacer.
    /// </summary>
    public const TaskFilter Default = TaskFilter.Pending;

    /// <summary>En el orden en que se enseñan.</summary>
    public static readonly TaskFilter[] All =
    [
        TaskFilter.All,
        TaskFilter.Pending,
        TaskFilter.Done,
        TaskFilter.Overdue,
        TaskFilter.StartedBefore,
        TaskFilter.StartsFromToday,
        TaskFilter.DueBefore,
        TaskFilter.DueFromToday,
    ];

    /// <summary>Clave de texto de cada filtro, para que las dos aplicaciones lo llamen igual.</summary>
    public static string KeyOf(TaskFilter filter) => filter switch
    {
        TaskFilter.Pending => "FilterPending",
        TaskFilter.Done => "FilterDone",
        TaskFilter.All => "FilterAll",
        TaskFilter.Overdue => "FilterOverdue",
        TaskFilter.StartedBefore => "FilterStartedBefore",
        TaskFilter.StartsFromToday => "FilterStartsFromToday",
        TaskFilter.DueBefore => "FilterDueBefore",
        _ => "FilterDueFromToday",
    };

    /// <summary>
    /// Si la tarea entra en el filtro. <paramref name="today"/> se pasa desde fuera para que una
    /// misma pantalla no cambie de criterio a medianoche mientras se esta mirando.
    /// </summary>
    public static bool Matches(TaskItem task, TaskFilter filter, DateTime today) => filter switch
    {
        TaskFilter.Pending => !task.IsDone,
        TaskFilter.Done => task.IsDone,
        TaskFilter.All => true,

        // Caducada es "se paso y sigue sin hacer": una tarea terminada tarde ya no urge a nadie.
        TaskFilter.Overdue => !task.IsDone && task.DueAt is { } d && d.Date < today,

        TaskFilter.StartedBefore => task.PlannedFor is { } p && p.Date < today,
        TaskFilter.StartsFromToday => task.PlannedFor is { } p2 && p2.Date >= today,
        TaskFilter.DueBefore => task.DueAt is { } d2 && d2.Date < today,
        _ => task.DueAt is { } d3 && d3.Date >= today,
    };
}
