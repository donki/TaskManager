namespace TaskManager.Core.Models;

/// <summary>Cada cuanto se repite una tarea.</summary>
public enum RecurrenceKind
{
    /// <summary>No se repite: al completarla se acabo.</summary>
    None = 0,

    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Yearly = 4,
}

/// <summary>
/// Repeticion de una tarea: cada cuanto vuelve a aparecer.
/// </summary>
/// <remarks>
/// Se guarda como tipo + intervalo (cada 2 semanas, cada 3 meses...) en vez de una regla RRULE
/// completa: cubre lo que la gente usa de verdad en una lista de tareas y se puede enseñar en una
/// frase, que es lo que importa para poder cambiarlo sin manual de instrucciones.
///
/// <para>La tarea completada **no se reabre**: queda hecha, como registro de que se hizo, y se crea
/// la siguiente. Asi el historial y las rachas cuentan cada vuelta, en vez de una sola tarea que
/// nunca se termina.</para>
/// </remarks>
public readonly record struct Recurrence(RecurrenceKind Kind, int Interval)
{
    public static readonly Recurrence None = new(RecurrenceKind.None, 0);

    public bool Repeats => Kind != RecurrenceKind.None && Interval > 0;

    /// <summary>
    /// Cuando toca la siguiente vez, contando desde <paramref name="from"/>. Para la mensual se
    /// respeta el ultimo dia del mes: el 31 de enero cada mes es el 28 (o 29) de febrero, no el 3
    /// de marzo.
    /// </summary>
    public DateTime Next(DateTime from) => Kind switch
    {
        RecurrenceKind.Daily => from.AddDays(Interval),
        RecurrenceKind.Weekly => from.AddDays(7 * Interval),
        RecurrenceKind.Monthly => from.AddMonths(Interval),
        RecurrenceKind.Yearly => from.AddYears(Interval),
        _ => from,
    };

    /// <summary>Como se guarda: <c>daily:1</c>, <c>weekly:2</c>... Vacio si no se repite.</summary>
    public string Serialize() => Repeats ? $"{Kind.ToString().ToLowerInvariant()}:{Interval}" : string.Empty;

    public static Recurrence Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return None;

        var parts = value.Split(':', 2);
        if (!Enum.TryParse<RecurrenceKind>(parts[0], ignoreCase: true, out var kind) || kind == RecurrenceKind.None)
            return None;

        var interval = parts.Length > 1 && int.TryParse(parts[1], out var parsed) && parsed > 0 ? parsed : 1;
        return new Recurrence(kind, interval);
    }

    /// <summary>Frase corta para la interfaz ("cada 2 semanas").</summary>
    public string Describe() => (Kind, Interval) switch
    {
        (RecurrenceKind.None, _) => "No se repite",
        (RecurrenceKind.Daily, 1) => "Cada día",
        (RecurrenceKind.Daily, var n) => $"Cada {n} días",
        (RecurrenceKind.Weekly, 1) => "Cada semana",
        (RecurrenceKind.Weekly, var n) => $"Cada {n} semanas",
        (RecurrenceKind.Monthly, 1) => "Cada mes",
        (RecurrenceKind.Monthly, var n) => $"Cada {n} meses",
        (RecurrenceKind.Yearly, 1) => "Cada año",
        (RecurrenceKind.Yearly, var n) => $"Cada {n} años",
        _ => "No se repite",
    };
}

/// <summary>
/// Etiquetas de una tarea. Se guardan en un solo campo separadas por comas y con comas tambien al
/// principio y al final (<c>,casa,urgente,</c>): asi filtrar por una etiqueta es un LIKE exacto
/// sobre <c>,etiqueta,</c> y no hace falta una tabla aparte ni sincronizar relaciones.
/// </summary>
public static class TaskTags
{
    public static IReadOnlyList<string> Split(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

    public static string Join(IEnumerable<string> tags)
    {
        var clean = tags
            .Select(t => t.Trim().Trim(','))
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return clean.Count == 0 ? string.Empty : "," + string.Join(",", clean) + ",";
    }

    /// <summary>Texto escrito a mano ("casa, urgente") convertido al formato de guardado.</summary>
    public static string FromInput(string? input) =>
        Join((input ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Como se le enseña al usuario para editarlo.</summary>
    public static string ToInput(string? stored) => string.Join(", ", Split(stored));

    public static bool Has(string? stored, string tag) =>
        Split(stored).Any(t => string.Equals(t, tag, StringComparison.CurrentCultureIgnoreCase));
}
