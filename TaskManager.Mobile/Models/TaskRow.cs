using TaskManager.Core.Models;

namespace TaskManager.Mobile.Models;

/// <summary>
/// Fila de tarea lista para pintar. Se aplana aqui lo que el XAML necesita para no meter logica en
/// la vista (constitucion 7).
/// </summary>
public sealed class TaskRow
{
    public TaskRow(TaskItem task, string listName = "")
    {
        Task = task;
        ListName = listName;
    }

    public TaskItem Task { get; }

    /// <summary>Micro-pasos ya cargados. Solo los rellena la pagina de detalle de una lista.</summary>
    public List<StepRow> Steps { get; init; } = [];

    public bool ShowSteps => Steps.Count > 0;

    public Guid Id => Task.Id;

    public string Title => Task.Title;

    public bool IsDone => Task.IsDone;

    public string ListName { get; }

    public bool ShowListName => ListName.Length > 0;

    /// <summary>Circulo vacio o con tick: el estado se ve de un vistazo sin leer nada.</summary>
    public string StateIcon => Task.IsDone ? "ic_circle_check.png" : "ic_circle.png";

    public TextDecorations Decoration => Task.IsDone ? TextDecorations.Strikethrough : TextDecorations.None;

    public double Opacity => Task.IsDone ? 0.55 : 1.0;

    public bool HasSteps => Task.StepCount > 0;

    public string StepsCaption => Task.StepCount > 0 ? $"{Task.StepsDone}/{Task.StepCount} pasos" : string.Empty;

    public double Progress => Task.Progress;

    public bool InMyDay => Task.MyDayOn?.Date == DateTime.Now.Date;

    public string MyDayIcon => InMyDay ? "ic_day.png" : "ic_star.png";
}

/// <summary>Fila de micro-paso ("Paso Magico").</summary>
public sealed class StepRow
{
    public StepRow(TaskStep step) => Step = step;

    public TaskStep Step { get; }

    public Guid Id => Step.Id;

    public string Title => Step.Title;

    public bool IsDone => Step.IsDone;

    public string StateIcon => Step.IsDone ? "ic_circle_check.png" : "ic_circle.png";

    public TextDecorations Decoration => Step.IsDone ? TextDecorations.Strikethrough : TextDecorations.None;

    public double Opacity => Step.IsDone ? 0.55 : 1.0;

    /// <summary>Los pasos que vienen de la IA se distinguen de los escritos a mano.</summary>
    public bool FromAi => Step.Source == "ai";
}
