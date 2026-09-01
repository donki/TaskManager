using System.ComponentModel;
using System.Runtime.CompilerServices;
using TaskManager.Core.Models;

namespace TaskManager.Mobile.Models;

/// <summary>
/// Fila de tarea lista para pintar. Se aplana aqui lo que el XAML necesita para no meter logica en
/// la vista (constitucion 7).
/// </summary>
public sealed class TaskRow : INotifyPropertyChanged
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

    /// <summary>
    /// Con estrella delante si es prioritaria. Delante del texto y no en una columna aparte: la fila
    /// ya lleva boton de estado y texto, y una columna vacia en casi todas las filas solo estrecha
    /// el titulo.
    /// </summary>
    public string Title => Task.IsPinned ? "📌 " + Task.Title : Task.Title;

    public bool IsDone => Task.IsDone;

    public string ListName { get; }

    public bool ShowListName => ListName.Length > 0;

    /// <summary>Circulo vacio o con tick: el estado se ve de un vistazo sin leer nada.</summary>
    public string StateIcon => Task.IsDone ? "ic_circle_check.png" : "ic_circle.png";

    public TextDecorations Decoration => Task.IsDone ? TextDecorations.Strikethrough : TextDecorations.None;

    public double Opacity => Task.IsDone ? 0.55 : 1.0;

    public bool HasSteps => Task.StepCount > 0;

    /// <summary>Etiquetas de la tarea, para verlas de un vistazo en la fila.</summary>
    public string TagsCaption => Task.TagList.Count > 0 ? "#" + string.Join("  #", Task.TagList) : string.Empty;

    public bool HasTags => Task.TagList.Count > 0;

    /// <summary>Plazo y repeticion, cuando los hay.</summary>
    public string ScheduleCaption
    {
        get
        {
            var parts = new List<string>();
            if (Task.PlannedFor is { } planned)
                parts.Add($"Plan: {(planned.Date == DateTime.Now.Date ? "hoy" : planned.ToString("d MMM"))}");
            if (Task.DueAt is { } due)
                parts.Add($"Vence: {(due.Date == DateTime.Now.Date ? "hoy" : due.ToString("d MMM"))}");
            if (Task.Recurrence.Repeats)
                parts.Add(Task.Recurrence.Describe().ToLowerInvariant());

            return string.Join(" · ", parts);
        }
    }

    public bool HasSchedule => ScheduleCaption.Length > 0;

    public string StepsCaption => Task.StepCount > 0 ? $"{Task.StepsDone}/{Task.StepCount} pasos" : string.Empty;

    public double Progress => Task.Progress;

    public bool InMyDay => Task.MyDayOn?.Date == DateTime.Now.Date;

    public string MyDayIcon => InMyDay ? "ic_day.png" : "ic_star.png";

    // -----------------------------------------------------------------------
    // Seleccion multiple
    // -----------------------------------------------------------------------
    //
    // Estas dos son las unicas que cambian sin recargar la lista, y por eso avisan. Repintar la
    // pantalla entera en cada toque perderia el sitio donde estaba el usuario, que es justo lo peor
    // que puede pasar mientras marca ocho tareas seguidas.

    private bool _selecting;
    private bool _isSelected;

    /// <summary>Si la lista esta en modo seleccion (entonces cada fila enseña su casilla).</summary>
    public bool Selecting
    {
        get => _selecting;
        set
        {
            if (_selecting == value)
            {
                return;
            }

            _selecting = value;
            Raise();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            Raise();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
