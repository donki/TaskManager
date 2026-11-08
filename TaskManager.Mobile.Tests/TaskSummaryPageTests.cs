using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Pages;
using TaskManager.Mobile.Tests.Infra;

namespace TaskManager.Mobile.Tests;

/// <summary>
/// El resumen de solo lectura de una tarea, con el lapiz aparte para editar, y el tema elegido
/// en Ajustes (2026-11-08).
/// </summary>
public class TaskSummaryPageTests
{
    private static async Task<TaskSummaryPage> OpenAsync(TaskItem task)
    {
        var page = new TaskSummaryPage { TaskId = task.Id.ToString() };
        await page.Appear();
        return page;
    }

    [Fact]
    public void Una_tarea_recien_escrita_enseña_estado_lista_y_la_pista() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Comprar pan");

        var page = await OpenAsync(tasks[0]);

        Assert.Equal("Comprar pan", page.Named<Label>("TitleLabel").Text);
        Assert.Equal(2, page.Named<VerticalStackLayout>("LinesHost").Count);
        Assert.True(page.Named<Label>("BareHint").IsVisible);
        Assert.False(page.Named<VisualElement>("StepsCard").IsVisible);
        Assert.False(page.Named<VisualElement>("NotesCard").IsVisible);
        Assert.Empty(app.Ui.Routes);
    });

    [Fact]
    public void Con_pasos_y_notas_los_enseña_y_el_lapiz_abre_el_editor() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Mudanza");
        var task = tasks[0];
        task.Notes = "Llamar antes";
        task.Tags = TaskTags.FromInput("casa");
        task.DueAt = DateTime.Today.AddDays(2);
        await app.Repository.UpdateTaskAsync(task);
        await app.Repository.AddStepsAsync(task.Id, ["Cajas", "Furgoneta"]);

        var page = await OpenAsync(task);

        Assert.False(page.Named<Label>("BareHint").IsVisible);
        Assert.True(page.Named<VisualElement>("StepsCard").IsVisible);
        Assert.Equal(2, page.Named<VerticalStackLayout>("StepsHost").Count);
        Assert.Equal("Llamar antes", page.Named<Label>("NotesLabel").Text);
        Assert.True(page.Named<VisualElement>("NotesCard").IsVisible);

        await page.Handler("OnEditClicked");
        Assert.Equal($"{nameof(TaskDetailPage)}?taskId={task.Id}", app.Ui.Routes.Single());
    });

    [Fact]
    public void Si_la_tarea_ya_no_existe_vuelve_atras() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var page = new TaskSummaryPage { TaskId = Guid.NewGuid().ToString() };
        await page.Appear();
        Assert.Equal([".."], app.Ui.Routes);

        // Sin id, el lapiz no hace nada.
        var empty = new TaskSummaryPage { TaskId = "no es un id" };
        await empty.Handler("OnEditClicked");
        Assert.Equal([".."], app.Ui.Routes);
    });

    [Fact]
    public void El_tema_se_elige_en_ajustes_y_se_aplica_al_momento() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var page = new SettingsPage();
        await page.Appear();

        var picker = page.Named<Picker>("ThemePicker");
        Assert.Equal([app.Texts["ThemeSystem"], app.Texts["ThemeLight"], app.Texts["ThemeDark"]],
            (List<string>)picker.ItemsSource);
        Assert.Equal(0, picker.SelectedIndex);
        Assert.Equal(AppTheme.Unspecified, app.Application!.UserAppTheme);

        picker.SelectedIndex = 2;
        await UiThread.IdleAsync();
        Assert.Equal(ThemeChoice.Dark, app.Settings.Theme);
        Assert.Equal(AppTheme.Dark, app.Application.UserAppTheme);

        picker.SelectedIndex = 1;
        await UiThread.IdleAsync();
        Assert.Equal(ThemeChoice.Light, app.Settings.Theme);
        Assert.Equal(AppTheme.Light, app.Application.UserAppTheme);

        picker.SelectedIndex = 0;
        await UiThread.IdleAsync();
        Assert.Equal(ThemeChoice.System, app.Settings.Theme);
        Assert.Equal(AppTheme.Unspecified, app.Application.UserAppTheme);

        // Al volver a la pantalla, sale lo guardado.
        await app.Settings.SetThemeAsync(ThemeChoice.Dark);
        await page.Appear();
        Assert.Equal(2, picker.SelectedIndex);
    });
}
