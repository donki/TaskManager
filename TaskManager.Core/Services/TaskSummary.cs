using TaskManager.Core.Models;

namespace TaskManager.Core.Services;

/// <summary>
/// Lo que se ve de una tarea de un vistazo, sin abrir el editor: estado, lista, fechas,
/// repeticion, etiquetas, pasos y notas. Solo lectura.
/// </summary>
/// <remarks>
/// <para>Lo pidio un probador (2026-11-08): al tocar una tarea queria verla, no caer en el
/// formulario de edicion cada vez, y tener un boton aparte para editar. El resumen sale de aqui
/// para que Android y Windows digan exactamente lo mismo; cada plataforma solo lo pinta.</para>
///
/// <para>Las filas vacias no se enseñan: una tarea recien escrita tiene solo titulo y estado, y
/// una lista de campos con guiones se leeria como un formulario a medio rellenar.</para>
/// </remarks>
public sealed class TaskSummary
{
    public required string Title { get; init; }

    /// <summary>Pares etiqueta / valor, en el orden en que se enseñan.</summary>
    public required IReadOnlyList<SummaryLine> Lines { get; init; }

    /// <summary>Los pasos, con su estado. Vacia si no tiene.</summary>
    public required IReadOnlyList<TaskStep> Steps { get; init; }

    public required string Notes { get; init; }

    /// <summary>
    /// Solo tiene titulo, estado y lista: ni fechas, ni repeticion, ni etiquetas, ni pasos, ni
    /// notas. Es como queda una tarea apuntada con la caja rapida.
    /// </summary>
    public required bool IsBare { get; init; }

    public static TaskSummary Build(
        TaskItem task,
        string? listName,
        IReadOnlyList<TaskStep> steps,
        LocalizationService texts,
        DateTime? today = null)
    {
        var now = (today ?? DateTime.Now).Date;
        var lines = new List<SummaryLine>
        {
            new(texts["SummaryStatus"], task.IsDone
                ? texts["StatusDone"]
                : task.InProgress ? texts["StatusInProgress"] : texts["StatusPending"]),
        };

        if (!string.IsNullOrWhiteSpace(listName))
        {
            lines.Add(new(texts["TaskList"], listName));
        }

        // Estado y lista los tiene toda tarea; lo que venga despues es lo que la hace «no vacia».
        var basics = lines.Count;

        if (task.PlannedFor is { } planned)
        {
            lines.Add(new(texts["PlannedFor"], Day(planned, now, texts)));
        }

        if (task.DueAt is { } due)
        {
            lines.Add(new(texts["DueDate"], Day(due, now, texts)));
        }

        if (task.Recurrence.Repeats)
        {
            lines.Add(new(texts["Recurrence"], task.Recurrence.Describe(texts)));
        }

        if (task.TagList.Count > 0)
        {
            lines.Add(new(texts["Tags"], "#" + string.Join("  #", task.TagList)));
        }

        var visibleSteps = steps.Where(s => !s.Deleted).OrderBy(s => s.SortOrder).ToList();
        if (visibleSteps.Count > 0)
        {
            lines.Add(new(texts["Steps"],
                texts.Format("StepsShort", visibleSteps.Count(s => s.IsDone), visibleSteps.Count)));
        }

        return new TaskSummary
        {
            Title = task.Title,
            Lines = lines,
            Steps = visibleSteps,
            Notes = task.Notes.Trim(),
            IsBare = lines.Count == basics && visibleSteps.Count == 0 && task.Notes.Trim().Length == 0,
        };
    }

    /// <summary>«hoy» con palabras; el resto, dia y mes (y el año si no es el actual).</summary>
    private static string Day(DateTime date, DateTime today, LocalizationService texts)
    {
        if (date.Date == today)
        {
            return texts["TodayWord"];
        }

        return date.Year == today.Year ? date.ToString("ddd d MMM") : date.ToString("d MMM yyyy");
    }
}

/// <summary>Una fila del resumen: nombre del campo y su valor ya escrito.</summary>
public sealed record SummaryLine(string Label, string Value);
