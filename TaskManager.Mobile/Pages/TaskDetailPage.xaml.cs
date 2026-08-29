using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// Detalle de una tarea: se edita lo que hay que hacer, el **contexto** que precisa el desglose,
/// las etiquetas, el plazo y cada cuanto se repite, y se ven y marcan sus micro-pasos.
/// </summary>
/// <remarks>
/// Nace de una nota de autor del 2026-08-29 ("permitir editar las tareas y ver los pasos
/// propuestos"): desde Mi Dia solo se podia completar una tarea, no tocarla.
/// </remarks>
[QueryProperty(nameof(TaskId), "taskId")]
public partial class TaskDetailPage : ContentPage
{
    private static readonly RecurrenceKind[] Kinds =
        [RecurrenceKind.None, RecurrenceKind.Daily, RecurrenceKind.Weekly, RecurrenceKind.Monthly, RecurrenceKind.Yearly];

    private readonly TaskService _tasks;
    private readonly SettingsService _settings;
    private readonly INotificationService _notifications;

    private TaskItem? _task;
    private Guid _taskId;
    private bool _loading;

    public TaskDetailPage()
        : this(ServiceHelper.GetRequiredService<TaskService>(),
               ServiceHelper.GetRequiredService<SettingsService>(),
               ServiceHelper.GetRequiredService<INotificationService>())
    {
    }

    public TaskDetailPage(TaskService tasks, SettingsService settings, INotificationService notifications)
    {
        InitializeComponent();

        _tasks = tasks;
        _settings = settings;
        _notifications = notifications;

        RecurrencePicker.ItemsSource = new List<string>
        {
            "No se repite", "Cada día", "Cada semana", "Cada mes", "Cada año",
        };
    }

    /// <summary>Llega por la ruta: <c>TaskDetailPage?taskId=...</c>.</summary>
    public string TaskId
    {
        set => _taskId = Guid.TryParse(Uri.UnescapeDataString(value), out var id) ? id : Guid.Empty;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        Celebration.HapticsEnabled = _settings.HapticsEnabled;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_taskId == Guid.Empty)
        {
            return;
        }

        _task = await _tasks.Repository.GetTaskAsync(_taskId);
        if (_task is null)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        // Bandera de carga: rellenar los controles dispara sus eventos, y sin esto se reescribiria
        // la tarea con lo que aun se esta pintando.
        _loading = true;

        Title = _task.Title;
        TitleEntry.Text = _task.Title;
        NotesEditor.Text = _task.Notes;
        ContextEditor.Text = _task.Context;
        TagsEntry.Text = TaskTags.ToInput(_task.Tags);

        // Rango acotado: sin el, el selector de Android abre la lista de años entera y cuesta
        // llegar al mes y al dia (nota de autor del 2026-08-29).
        var floor = DateTime.Now.Date.AddYears(-1);
        var ceiling = DateTime.Now.Date.AddYears(5);

        DuePicker.MinimumDate = floor;
        DuePicker.MaximumDate = ceiling;
        PlannedPicker.MinimumDate = floor;
        PlannedPicker.MaximumDate = ceiling;

        DueSwitch.IsToggled = _task.DueAt is not null;
        DuePicker.IsVisible = _task.DueAt is not null;
        DuePicker.Date = _task.DueAt?.Date ?? DateTime.Now.Date;

        PlannedSwitch.IsToggled = _task.PlannedFor is not null;
        PlannedPicker.IsVisible = _task.PlannedFor is not null;
        PlannedPicker.Date = _task.PlannedFor?.Date ?? DateTime.Now.Date;

        var recurrence = _task.Recurrence;
        RecurrencePicker.SelectedIndex = Array.IndexOf(Kinds, recurrence.Kind) is var index && index >= 0 ? index : 0;
        IntervalStepper.Value = Math.Clamp(recurrence.Interval <= 0 ? 1 : recurrence.Interval, 1, 30);
        IntervalStepper.IsVisible = recurrence.Kind != RecurrenceKind.None;
        RecurrenceLabel.Text = recurrence.Describe();

        _loading = false;

        await LoadStepsAsync();
    }

    // ==================================================================================
    //  Pasos
    // ==================================================================================

    private async Task LoadStepsAsync()
    {
        if (_task is null)
        {
            return;
        }

        var steps = await _tasks.Repository.GetStepsAsync(_task.Id);

        StepsBox.Clear();
        NoStepsLabel.IsVisible = steps.Count == 0;

        foreach (var step in steps)
        {
            StepsBox.Add(BuildStepRow(step));
        }
    }

    /// <summary>Fila de paso: icono de estado, titulo tachado si esta hecho y marca de si vino de la IA.</summary>
    private View BuildStepRow(TaskStep step)
    {
        var toggle = new ImageButton
        {
            Source = step.IsDone ? "ic_circle_check.png" : "ic_circle.png",
            Style = (Style)Application.Current!.Resources["RowIconButton"],
            HeightRequest = 34,
            WidthRequest = 34,
            CommandParameter = step.Id,
        };
        toggle.Clicked += OnToggleStepClicked;

        var label = new Label
        {
            Text = step.Title,
            VerticalOptions = LayoutOptions.Center,
            Opacity = step.IsDone ? 0.55 : 1,
            TextDecorations = step.IsDone ? TextDecorations.Strikethrough : TextDecorations.None,
        };

        var row = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 6,
            Padding = new Thickness(0, 2),
        };

        row.Add(toggle, 0, 0);
        row.Add(label, 1, 0);
        return row;
    }

    private async void OnToggleStepClicked(object? sender, EventArgs e)
    {
        if (sender is not ImageButton { CommandParameter: Guid id })
        {
            return;
        }

        var step = await _tasks.Repository.GetStepAsync(id);
        if (step is null)
        {
            return;
        }

        var celebration = await _tasks.ToggleStepAsync(step);
        await LoadStepsAsync();

        if (celebration is not null)
        {
            Celebration.Celebrate(celebration);
        }
    }

    /// <summary>
    /// Propone pasos a partir del titulo y del contexto. Antes de proponer se guarda lo escrito:
    /// de nada sirve un contexto que todavia esta solo en pantalla.
    /// </summary>
    private async void OnBreakdownClicked(object? sender, EventArgs e)
    {
        if (_task is null)
        {
            return;
        }

        await SaveAsync(silent: true);

        WandButton.IsEnabled = false;
        StepsBusy.IsRunning = true;
        StepsBusy.IsVisible = true;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var proposal = await _tasks.ProposeBreakdownAsync(_task, cts.Token);

            if (!proposal.HasSomethingNew)
            {
                await SocShared.ModernDialog.AlertAsync(this, "Pasos Mágicos",
                    proposal.AlreadyPresent > 0
                        ? "Los pasos propuestos ya están en la tarea."
                        : "No ha salido ningún paso esta vez.",
                    "OK");
                return;
            }

            var detail = "• " + string.Join("\n• ", proposal.Steps);
            if (proposal.AlreadyPresent > 0)
            {
                detail += $"\n\n({proposal.AlreadyPresent} ya estaban y se han descartado)";
            }

            var accepted = await SocShared.ModernDialog.AlertAsync(this,
                $"Pasos Mágicos · {proposal.Source}", detail, "Añadir", "Ahora no");

            if (!accepted)
            {
                return;
            }

            var (_, celebration) = await _tasks.ApplyBreakdownAsync(_task, proposal.Steps);
            await LoadStepsAsync();

            if (celebration is not null)
            {
                Celebration.Celebrate(celebration);
            }
        }
        finally
        {
            StepsBusy.IsRunning = false;
            StepsBusy.IsVisible = false;
            WandButton.IsEnabled = true;
        }
    }

    // ==================================================================================
    //  Plazo y repeticion
    // ==================================================================================

    private void OnDueToggled(object? sender, ToggledEventArgs e)
    {
        DuePicker.IsVisible = e.Value;
    }

    private void OnPlannedToggled(object? sender, ToggledEventArgs e)
    {
        PlannedPicker.IsVisible = e.Value;
    }

    private void OnRecurrenceChanged(object? sender, EventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var kind = Kinds[Math.Clamp(RecurrencePicker.SelectedIndex, 0, Kinds.Length - 1)];
        IntervalStepper.IsVisible = kind != RecurrenceKind.None;
        RecurrenceLabel.Text = new Recurrence(kind, (int)IntervalStepper.Value).Describe();
    }

    private void OnIntervalChanged(object? sender, ValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var kind = Kinds[Math.Clamp(RecurrencePicker.SelectedIndex, 0, Kinds.Length - 1)];
        RecurrenceLabel.Text = new Recurrence(kind, (int)e.NewValue).Describe();
    }

    // ==================================================================================
    //  Guardar y borrar
    // ==================================================================================

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (await SaveAsync(silent: false))
        {
            await Shell.Current.GoToAsync("..");
        }
    }

    private async Task<bool> SaveAsync(bool silent)
    {
        if (_task is null)
        {
            return false;
        }

        var title = TitleEntry.Text?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            if (!silent)
            {
                await SocShared.ModernDialog.AlertAsync(this, "Falta el título",
                    "La tarea necesita un título.", "OK");
            }

            return false;
        }

        _task.Title = title;
        _task.Notes = NotesEditor.Text?.Trim() ?? string.Empty;
        _task.Context = ContextEditor.Text?.Trim() ?? string.Empty;
        _task.Tags = TaskTags.FromInput(TagsEntry.Text);
        // DatePicker.Date es nullable desde MAUI 10.
        _task.DueAt = DueSwitch.IsToggled ? DuePicker.Date?.Date : null;
        _task.PlannedFor = PlannedSwitch.IsToggled ? PlannedPicker.Date?.Date : null;

        var kind = Kinds[Math.Clamp(RecurrencePicker.SelectedIndex, 0, Kinds.Length - 1)];
        _task.RecurrenceRule = new Recurrence(kind, (int)IntervalStepper.Value).Serialize();

        await _tasks.Repository.UpdateTaskAsync(_task);

        // El aviso se reprograma con lo que acaba de guardarse: si se quito la fecha, se cancela.
        _notifications.ScheduleTaskReminder(_task);
        return true;
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_task is null)
        {
            return;
        }

        var confirmed = await SocShared.ModernDialog.AlertAsync(this, "Borrar tarea",
            $"Se borrará «{_task.Title}» y sus pasos.", "Borrar", "Cancelar");

        if (!confirmed)
        {
            return;
        }

        await _tasks.Repository.DeleteTaskAsync(_task);
        await Shell.Current.GoToAsync("..");
    }
}
