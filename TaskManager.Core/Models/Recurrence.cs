using System.Linq;

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
/// <param name="Days">
/// Dias de la semana en los que puede caer, como mascara de bits (bit 0 = domingo … bit 6 = sabado).
/// <b>Cero significa «cualquier dia»</b>, que es lo que vale para las repeticiones mensuales y
/// anuales y para quien no quiera afinar. Solo se tiene en cuenta en la diaria y la semanal: decir
/// «cada 3 meses, pero solo en martes» no describe nada que la gente quiera de verdad.
/// </param>
/// <param name="MonthDay">
/// Dia del mes en el que cae la repeticion mensual (1-31). <b>Cero significa «el mismo dia que
/// tenia»</b>, que es como se comportaba antes y sigue siendo lo razonable si nadie dice otra cosa.
/// Solo se tiene en cuenta en la mensual.
/// </param>
/// <param name="Month">
/// Mes en el que cae la repeticion anual (1-12). <b>Cero significa «el mismo mes que tenia»</b>.
/// Solo se tiene en cuenta en la anual, donde acompaña a <paramref name="MonthDay"/>.
/// </param>
public readonly record struct Recurrence(
    RecurrenceKind Kind, int Interval, byte Days = 0, byte MonthDay = 0, byte Month = 0)
{
    public static readonly Recurrence None = new(RecurrenceKind.None, 0);

    /// <summary>Los tipos donde elegir dias de la semana tiene sentido.</summary>
    public bool UsesDays => Kind is RecurrenceKind.Daily or RecurrenceKind.Weekly;

    /// <summary>La mensual y la anual admiten fijar el dia del mes.</summary>
    public bool UsesMonthDay => Kind is RecurrenceKind.Monthly or RecurrenceKind.Yearly;

    /// <summary>Solo la anual admite ademas elegir el mes.</summary>
    public bool UsesMonth => Kind == RecurrenceKind.Yearly;

    public bool Repeats => Kind != RecurrenceKind.None && Interval > 0;

    /// <summary>Si ese dia de la semana esta elegido. Sin ninguno elegido, valen todos.</summary>
    public bool Includes(DayOfWeek day) => Days == 0 || (Days & (1 << (int)day)) != 0;

    public static byte MaskOf(IEnumerable<DayOfWeek> days)
    {
        byte mask = 0;
        foreach (var day in days)
        {
            mask |= (byte)(1 << (int)day);
        }

        return mask;
    }

    /// <summary>
    /// Cuando toca la siguiente vez, contando desde <paramref name="from"/>. Para la mensual se
    /// respeta el ultimo dia del mes: el 31 de enero cada mes es el 28 (o 29) de febrero, no el 3
    /// de marzo.
    /// </summary>
    public DateTime Next(DateTime from)
    {
        var next = Kind switch
        {
            RecurrenceKind.Daily => from.AddDays(Interval),
            RecurrenceKind.Weekly => from.AddDays(7 * Interval),
            RecurrenceKind.Monthly => MonthlyNext(from),
            RecurrenceKind.Yearly => YearlyNext(from),
            _ => from,
        };

        if (!UsesDays || Days == 0)
        {
            return next;
        }

        // Se avanza dia a dia hasta caer en uno de los elegidos. El tope de 7 vueltas es la
        // salvaguarda de que la mascara no se quede vacia por un valor corrupto y esto no termine
        // nunca; con al menos un dia marcado siempre se encuentra antes.
        for (var i = 0; i < 7 && !Includes(next.DayOfWeek); i++)
        {
            next = next.AddDays(1);
        }

        return next;
    }

    /// <summary>
    /// Cuando toca la siguiente mensual. Si hay dia fijado se va a ese dia del mes que corresponda,
    /// y si ese mes no llega —el 31 en febrero— se queda en el <b>ultimo dia del mes</b>, que es lo
    /// que la gente entiende por «el 31»: el final, no el 3 de marzo.
    /// </summary>
    private DateTime MonthlyNext(DateTime from)
    {
        var target = from.AddMonths(Interval);

        if (MonthDay == 0)
        {
            return target;
        }

        var day = Math.Min(MonthDay, DateTime.DaysInMonth(target.Year, target.Month));
        return new DateTime(target.Year, target.Month, day, target.Hour, target.Minute, 0, target.Kind);
    }

    /// <summary>
    /// Cuando toca la siguiente anual. Con mes y dia fijados va a esa fecha exacta del año que
    /// corresponda; si ese año no llega el dia —el 29 de febrero— se queda en el ultimo del mes.
    /// </summary>
    private DateTime YearlyNext(DateTime from)
    {
        var target = from.AddYears(Interval);

        if (Month == 0 && MonthDay == 0)
        {
            return target;
        }

        var month = Month == 0 ? target.Month : Math.Clamp((int)Month, 1, 12);
        var day = MonthDay == 0 ? target.Day : (int)MonthDay;
        day = Math.Min(day, DateTime.DaysInMonth(target.Year, month));

        return new DateTime(target.Year, month, day, target.Hour, target.Minute, 0, target.Kind);
    }

    /// <summary>Como se guarda: <c>daily:1</c>, <c>weekly:2</c>, y con dias <c>weekly:1:62</c>. La tercera
    /// parte solo aparece si hay dias elegidos, asi que lo guardado antes se sigue leyendo igual.
    /// </summary>
    public string Serialize()
    {
        if (!Repeats)
        {
            return string.Empty;
        }

        var head = $"{Kind.ToString().ToLowerInvariant()}:{Interval}";

        if (UsesDays && Days != 0)
        {
            return $"{head}:{Days}";
        }

        // La anual necesita dos numeros (mes y dia) y usa una parte mas: `yearly:1:9:15`.
        if (UsesMonth && (Month != 0 || MonthDay != 0))
        {
            return $"{head}:{Month}:{MonthDay}";
        }

        // El dia del mes reutiliza la tercera parte: los dias de la semana y el dia del mes nunca
        // conviven, porque uno es de la diaria/semanal y el otro de la mensual.
        return UsesMonthDay && MonthDay != 0 ? $"{head}:{MonthDay}" : head;
    }

    public static Recurrence Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return None;

        var parts = value.Split(':', 4);
        if (!Enum.TryParse<RecurrenceKind>(parts[0], ignoreCase: true, out var kind) || kind == RecurrenceKind.None)
            return None;

        var interval = parts.Length > 1 && int.TryParse(parts[1], out var parsed) && parsed > 0 ? parsed : 1;
        var extra = parts.Length > 2 && byte.TryParse(parts[2], out var parsedExtra) ? parsedExtra : (byte)0;

        // Las partes de mas significan una cosa u otra segun el tipo (ver Serialize).
        if (kind == RecurrenceKind.Yearly)
        {
            var day = parts.Length > 3 && byte.TryParse(parts[3], out var parsedDay) ? parsedDay : (byte)0;
            return new Recurrence(kind, interval, 0,
                Math.Clamp(day, (byte)0, (byte)31), Math.Clamp(extra, (byte)0, (byte)12));
        }

        return kind == RecurrenceKind.Monthly
            ? new Recurrence(kind, interval, 0, Math.Clamp(extra, (byte)0, (byte)31))
            : new Recurrence(kind, interval, extra);
    }

    /// <summary>Frase corta para la interfaz ("cada 2 semanas · L M X").</summary>
    public string Describe()
    {
        var basic = DescribeKind();

        if (UsesMonth && (Month != 0 || MonthDay != 0))
        {
            var months = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.MonthNames;
            var when = Month != 0 ? months[Month - 1] : string.Empty;

            return MonthDay != 0 ? $"{basic} · {MonthDay} {when}".TrimEnd() : $"{basic} · {when}";
        }

        if (UsesMonthDay && MonthDay != 0)
        {
            return $"{basic} · dia {MonthDay}";
        }

        if (!UsesDays || Days == 0)
        {
            return basic;
        }

        // La mascara se copia a una local: dentro de una struct, una lambda no puede tocar `this`.
        var mask = Days;
        var names = new[] { "D", "L", "M", "X", "J", "V", "S" };
        var chosen = Enumerable.Range(0, 7)
            .Where(i => (mask & (1 << i)) != 0)
            .Select(i => names[i]);

        return $"{basic} · {string.Join(" ", chosen)}";
    }

    private string DescribeKind() => (Kind, Interval) switch
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
