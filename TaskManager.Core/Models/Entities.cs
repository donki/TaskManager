using SQLite;

namespace TaskManager.Core.Models;

/// <summary>
/// Grupo compartido. La clave compartida NO se guarda en el dispositivo: se usa una vez al crear
/// o al unirse y lo que queda es la pertenencia (ver ARQUITECTURA.md, seccion 4).
/// </summary>
[Table("groups")]
public class TaskGroup
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Codigo publico de 6 caracteres, el que se dicta por telefono.</summary>
    public string JoinCode { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool Deleted { get; set; }
}

[Table("group_members")]
public class GroupMember
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;   // groupId|userId

    [Indexed]
    public Guid GroupId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Role { get; set; } = "member";

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Lista de tareas. <see cref="GroupId"/> nulo = lista privada del usuario.
/// </summary>
[Table("task_lists")]
public class TaskList
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Indexed]
    public Guid? GroupId { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Icon { get; set; } = "ic_list";

    public int SortOrder { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool Deleted { get; set; }

    [Ignore]
    public bool IsPrivate => GroupId is null;
}

[Table("tasks")]
public class TaskItem
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Indexed]
    public Guid ListId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    public DateTime? DoneAt { get; set; }

    public string? DoneBy { get; set; }

    /// <summary>
    /// Dia para el que la tarea esta en "Mi Dia". Nulo = no esta. El reinicio de medianoche no
    /// borra nada: al dia siguiente esta fecha ya no es hoy.
    /// </summary>
    [Indexed]
    public DateTime? MyDayOn { get; set; }

    public DateTime? DueAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool Deleted { get; set; }

    /// <summary>Se marca al desglosar con IA para no pagar XP dos veces por la misma tarea.</summary>
    public bool BreakdownRewarded { get; set; }

    [Ignore]
    public int StepCount { get; set; }

    [Ignore]
    public int StepsDone { get; set; }

    [Ignore]
    public double Progress => StepCount == 0 ? (IsDone ? 1 : 0) : (double)StepsDone / StepCount;
}

[Table("task_steps")]
public class TaskStep
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Indexed]
    public Guid TaskId { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    public int SortOrder { get; set; }

    /// <summary>"ai" o "manual": distingue los Pasos Magicos de los escritos a mano.</summary>
    public string Source { get; set; } = "manual";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool Deleted { get; set; }
}

public enum XpKind
{
    Task,
    Step,
    Breakdown,
    Bonus,
}

[Table("xp_events")]
public class XpEvent
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    [Indexed]
    public Guid? GroupId { get; set; }

    public Guid? TaskId { get; set; }

    public int Amount { get; set; }

    public XpKind Kind { get; set; }

    public double Combo { get; set; } = 1.0;

    [Indexed]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Ajuste local (clave/valor). Comparte formato entre movil y escritorio.</summary>
[Table("settings")]
public class SettingEntry
{
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Cambio pendiente de subir. La interfaz nunca espera a la red: escribe en SQLite y deja aqui la
/// intencion, que <c>ISyncService</c> vacia cuando hay conexion.
/// </summary>
[Table("sync_queue")]
public class SyncOp
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Entity { get; set; } = string.Empty;   // task_lists, tasks, task_steps, xp_events

    public string EntityId { get; set; } = string.Empty;

    public string Operation { get; set; } = "upsert";    // upsert | delete

    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;

    public int Attempts { get; set; }
}
