using TaskManager.Core.Data;

namespace TaskManager.Mobile.Helpers;

/// <summary>
/// El boton «+» de las cajas de apuntar: abre el formulario de una tarea nueva.
/// </summary>
/// <remarks>
/// Desde la 2026.11.08.00 el «+» hacia lo mismo que Intro (apuntar y quedarse en la lista) y con la
/// caja vacia no hacia nada; Josep lo echo en falta el 2026-11-23. Ahora: Intro apunta rapido, como
/// pidio el probador, y el «+» abre el detalle, con lo escrito como titulo o vacio. La tarea nace como
/// borrador: si se sale sin guardar, el detalle la borra (<c>TaskDetailPage.DropDraftAsync</c>).
/// </remarks>
public static class NewTaskForm
{
    public static async Task OpenAsync(TaskRepository repository, Guid listId, string? title,
        DateTime? plannedFor = null, bool inProgress = false)
    {
        var task = await repository.AddTaskAsync(listId, title?.Trim() ?? string.Empty,
            plannedFor: plannedFor, inProgress: inProgress);

        await Ui.Platform.GoToAsync($"{nameof(Pages.TaskDetailPage)}?taskId={task.Id}&isNew=1");
    }
}
