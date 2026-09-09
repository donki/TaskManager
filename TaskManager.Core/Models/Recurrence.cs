using System.Linq;
using TaskManager.Core.Services;

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
/// <para>La tarea completada **no se reabre**: queda hecha, como registro de que se hizo. Asi el
/// historial y las rachas cuentan cada vuelta, en vez de una sola tarea que nunca se termina.</para>
///
/// <para>Las vueltas se escriben **todas de una vez** cuando se guarda la tarea, una por cada dia en
/// que toca entre la fecha de planificacion y la de finalizacion (ver <see cref="Occurrences"/> y
/// <c>TaskService.GenerateSeriesAsync</c>), y por eso las dos fechas son obligatorias en cuanto hay
/// repeticion. <see cref="Next"/> —la siguiente vuelta a partir de una fecha— se sigue usando para
/// las tareas repetitivas que no son de ninguna serie.</para>
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
    /// Tope de tareas que puede crear una serie de una sola vez.
    /// </summary>
    /// <remarks>
    /// Una repeticion diaria a cinco años son 1826 tareas: se escriben, se sincronizan y hay que
    /// mirarlas todos los dias. Quien quiera eso puede alargar el rango otra vez cuando llegue.
    /// </remarks>
    public const int MaxOccurrences = 500;

    /// <summary>
    /// Todos los dias en que toca, entre <paramref name="from"/> y <paramref name="to"/> incluidos.
    /// </summary>
    /// <remarks>
    /// <para>Es lo que convierte «cada semana los martes, de octubre a diciembre» en las trece
    /// tareas que de verdad hay que hacer. <see cref="Next"/> responde a otra pregunta —cual es la
    /// siguiente— y no sirve para esto: con varios dias marcados va saltando de semana en semana y
    /// se deja el miercoles y el viernes por el camino.</para>
    ///
    /// <para>Los dias marcados mandan sobre el intervalo en la semanal: «cada 2 semanas, L y X» son
    /// el lunes y el miercoles de una semana de cada dos. En la diaria, marcar dias es una criba
    /// sobre el paso: lo que caiga en un dia no marcado se salta.</para>
    /// </remarks>
    public IEnumerable<DateTime> Occurrences(DateTime from, DateTime to)
    {
        if (!Repeats || to.Date < from.Date)
        {
            yield break;
        }

        var start = FirstFrom(from.Date);
        var end = to.Date;
        var count = 0;

        // La semanal se recorre por semanas y no por dias sueltos: dentro de cada semana que toca
        // valen todos los dias marcados, y las semanas intermedias se saltan enteras.
        if (Kind == RecurrenceKind.Weekly)
        {
            // El ciclo se cuenta desde el primer dia que TOCA, no desde el dia en que se empieza a
            // contar. «Cada 2 semanas los martes» a partir de un jueves 1 de octubre son el 6, el
            // 20, el 3 de noviembre…: anclando la cuenta a la semana del jueves, el martes de esa
            // semana ya habia pasado y la serie se saltaba entera la primera semana buena.
            var first = start;

            if (Days != 0)
            {
                for (var i = 0; i < 7 && !Includes(first.DayOfWeek); i++)
                {
                    first = first.AddDays(1);
                }
            }

            for (var week = first; week <= end && count < MaxOccurrences; week = week.AddDays(7 * Interval))
            {
                // El lunes de esa semana, para recorrerla entera: con varios dias marcados valen
                // todos los de la semana que toca, no solo el que abre el ciclo.
                var monday = week.AddDays(-(((int)week.DayOfWeek + 6) % 7));

                for (var i = 0; i < 7; i++)
                {
                    var day = monday.AddDays(i);

                    if (day >= first && day <= end &&
                        (Days == 0 ? day.DayOfWeek == first.DayOfWeek : Includes(day.DayOfWeek)))
                    {
                        count++;
                        yield return day;

                        if (count >= MaxOccurrences)
                        {
                            yield break;
                        }
                    }
                }
            }

            yield break;
        }

        for (var day = start; day <= end && count < MaxOccurrences;)
        {
            if (!UsesDays || Includes(day.DayOfWeek))
            {
                count++;
                yield return day;
            }

            var next = Kind switch
            {
                RecurrenceKind.Daily => day.AddDays(Interval),
                RecurrenceKind.Monthly => MonthlyNext(day),
                RecurrenceKind.Yearly => YearlyNext(day),
                _ => day.AddDays(1),
            };

            // Salvaguarda: si una regla corrupta no avanza, esto no puede quedarse dando vueltas.
            day = next > day ? next : day.AddDays(1);
        }
    }

    /// <summary>
    /// La primera fecha en que toca a partir de <paramref name="start"/>, para la mensual y la
    /// anual con dia fijado.
    /// </summary>
    /// <remarks>
    /// «Cada año el 15 de septiembre» empezando a contar un 1 de octubre tiene que caer en el 15 de
    /// septiembre siguiente, no en el 1 de octubre: sin esto, la serie entera se generaria en el dia
    /// en que se creo, que es justo el dia que el usuario no eligio.
    /// </remarks>
    private DateTime FirstFrom(DateTime start)
    {
        if (Kind == RecurrenceKind.Monthly && MonthDay != 0)
        {
            var candidate = OnDay(start, start.Month);
            return candidate >= start ? candidate : OnDay(start.AddMonths(1), start.AddMonths(1).Month);
        }

        if (Kind == RecurrenceKind.Yearly && (Month != 0 || MonthDay != 0))
        {
            var month = Month == 0 ? start.Month : Month;
            var candidate = OnDay(start, month);
            return candidate >= start ? candidate : OnDay(start.AddYears(1), month);
        }

        return start;
    }

    /// <summary>El dia fijado dentro de ese mes, sin pasarse del final (el 31 en febrero es el 28).</summary>
    private DateTime OnDay(DateTime reference, int month)
    {
        var day = MonthDay == 0 ? reference.Day : MonthDay;
        day = Math.Min(day, DateTime.DaysInMonth(reference.Year, month));

        return new DateTime(reference.Year, month, day);
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

    /// <summary>
    /// Frase corta para la interfaz («cada 2 semanas · L M X»).
    /// </summary>
    /// <remarks>
    /// Los textos vienen de fuera y no estan escritos aqui: esto lo pinta la lista de tareas y la
    /// ficha de una tarea, y estaba en castellano a pelo. Con la aplicacion en ingles se leia «Cada
    /// mes» debajo de un titulo en ingles.
    /// </remarks>
    public string Describe(LocalizationService textos)
    {
        var basic = DescribeKind(textos);

        if (UsesMonth && (Month != 0 || MonthDay != 0))
        {
            var months = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.MonthNames;
            var when = Month != 0 ? months[Month - 1] : string.Empty;

            return MonthDay != 0 ? $"{basic} · {MonthDay} {when}".TrimEnd() : $"{basic} · {when}";
        }

        if (UsesMonthDay && MonthDay != 0)
        {
            return $"{basic} · {textos.Format("RepeatOnDay", MonthDay)}";
        }

        if (!UsesDays || Days == 0)
        {
            return basic;
        }

        // La mascara se copia a una local: dentro de una struct, una lambda no puede tocar `this`.
        var mask = Days;
        var names = textos["WeekdayInitials"].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chosen = Enumerable.Range(0, 7)
            .Where(i => (mask & (1 << i)) != 0)
            .Select(i => names[i]);

        return $"{basic} · {string.Join(" ", chosen)}";
    }

    private string DescribeKind(LocalizationService t) => (Kind, Interval) switch
    {
        (RecurrenceKind.None, _) => t["RepeatNever"],
        (RecurrenceKind.Daily, 1) => t["RepeatDaily"],
        (RecurrenceKind.Daily, var n) => t.Format("RepeatDailyN", n),
        (RecurrenceKind.Weekly, 1) => t["RepeatWeekly"],
        (RecurrenceKind.Weekly, var n) => t.Format("RepeatWeeklyN", n),
        (RecurrenceKind.Monthly, 1) => t["RepeatMonthly"],
        (RecurrenceKind.Monthly, var n) => t.Format("RepeatMonthlyN", n),
        (RecurrenceKind.Yearly, 1) => t["RepeatYearly"],
        (RecurrenceKind.Yearly, var n) => t.Format("RepeatYearlyN", n),
        _ => t["RepeatNever"],
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
