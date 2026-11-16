using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// Resumen de una tarea, de solo lectura, con un boton aparte para editar.
/// </summary>
/// <remarks>
/// Tocar una tarea en cualquier lista lleva aqui; el lapiz de la cabecera lleva al detalle
/// (<see cref="TaskDetailPage"/>). Al volver del detalle se recarga, para enseñar lo guardado.
/// </remarks>
[QueryProperty(nameof(TaskId), "taskId")]
public partial class TaskSummaryPage : ContentPage
{
    private readonly TaskService _tasks;
    private Guid _taskId;

    public TaskSummaryPage()
        : this(ServiceHelper.GetRequiredService<TaskService>())
    {
    }

    public TaskSummaryPage(TaskService tasks)
    {
        InitializeComponent();
        _tasks = tasks;
    }

    /// <summary>La ruta de esta pantalla para una tarea. Todas las listas abren las tareas por aqui.</summary>
    public static string Route(Guid taskId) => $"{nameof(TaskSummaryPage)}?taskId={taskId}";

    public string TaskId
    {
        get => _taskId.ToString();
        set => _taskId = Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    /// <summary>
    /// Lee la tarea y la pinta. Si ya no existe (borrada desde el detalle o desde otro
    /// dispositivo), se vuelve atras: no hay nada que resumir.
    /// </summary>
    internal async Task LoadAsync()
    {
        var task = _taskId == Guid.Empty ? null : await _tasks.Repository.GetTaskAsync(_taskId);
        if (task is null || task.Deleted)
        {
            await Ui.Platform.GoToAsync("..");
            return;
        }

        var list = await _tasks.Repository.GetListAsync(task.ListId);
        var steps = await _tasks.Repository.GetStepsAsync(task.Id);
        Show(TaskSummary.Build(task, list?.Name, steps, Localization.Loc.Instance.Textos));
    }

    private void Show(TaskSummary summary)
    {
        TitleLabel.Text = summary.Title;

        LinesHost.Children.Clear();
        foreach (var line in summary.Lines)
        {
            var row = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 12 };
            row.Add(new Label { Text = line.Label, Style = AppStyle("ItemSubtitle"), VerticalOptions = LayoutOptions.Start }, 0);
            row.Add(new Label { Text = line.Value, LineBreakMode = LineBreakMode.WordWrap, HorizontalTextAlignment = TextAlignment.End }, 1);
            LinesHost.Children.Add(row);
        }

        BareHint.IsVisible = summary.IsBare;

        StepsHost.Children.Clear();
        foreach (var step in summary.Steps)
        {
            // El estado con el mismo circulo (con o sin tick) que las filas de la lista, no con los
            // caracteres de tick y de circulo: cada telefono los dibuja a su manera (General 6.2).
            var row = new Grid
            {
                ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)],
                ColumnSpacing = 8,
                Opacity = step.IsDone ? 0.6 : 1,
            };
            row.Add(new Image
            {
                Source = step.IsDone ? "ic_circle_check.png" : "ic_circle.png",
                WidthRequest = 18,
                HeightRequest = 18,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 1, 0, 0),
            }, 0);
            row.Add(new Label
            {
                Text = step.Title,
                TextDecorations = step.IsDone ? TextDecorations.Strikethrough : TextDecorations.None,
                LineBreakMode = LineBreakMode.WordWrap,
            }, 1);
            StepsHost.Children.Add(row);
        }

        StepsCard.IsVisible = summary.Steps.Count > 0;

        NotesLabel.Text = summary.Notes;
        NotesCard.IsVisible = summary.Notes.Length > 0;
    }

    /// <summary>Un estilo de la aplicacion; sin aplicacion (pruebas), ninguno.</summary>
    private static Style? AppStyle(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var style) == true ? style as Style : null;

    /// <summary>El lapiz: al formulario de edicion de siempre.</summary>
    private async void OnEditClicked(object? sender, EventArgs e)
    {
        if (_taskId != Guid.Empty)
        {
            await Ui.Platform.GoToAsync($"{nameof(TaskDetailPage)}?taskId={_taskId}");
        }
    }
}
