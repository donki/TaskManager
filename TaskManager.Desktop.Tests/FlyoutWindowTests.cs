using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Desktop.Services;
using TaskManager.Desktop.Tests.Banco;

namespace TaskManager.Desktop.Tests;

/// <summary>El panel rapido de la bandeja.</summary>
public class FlyoutWindowTests
{
    private static async Task<(Datos Datos, FlyoutWindow Panel, List<int> Pendientes)> AbrirAsync(
        Func<Datos, Task>? preparar = null, SyncCoordinator? syncing = null)
    {
        var datos = await Ui.Datos();
        if (preparar is not null)
        {
            await preparar(datos);
        }

        var panel = new FlyoutWindow(datos.Tasks, datos.Settings, syncing);
        var pendientes = new List<int>();
        panel.PendingChanged += (_, n) => pendientes.Add(n);
        await Ui.Hacer(panel.ShowFlyout);
        await Ui.Hasta(() => panel.StatusLabel.Text.Length > 0);
        return (datos, panel, pendientes);
    }

    private static List<string> Titulos(FlyoutWindow p) => [.. p.TaskList.Items.Cast<FlyoutWindow.TaskRow>().Select(r => r.Title)];

    [Fact]
    public Task Enseña_lo_pendiente_de_todas_las_listas_y_cuenta() => Ui.Run(async () =>
    {
        var (datos, panel, pendientes) = await AbrirAsync(async d =>
        {
            var casa = await d.Repo.CreateListAsync("Casa");
            var grupo = await d.Repo.SaveGroupAsync(new TaskGroup { Name = "Familia" });
            var compra = await d.Repo.CreateListAsync("Compra", grupo.Id);
            var t = await d.Repo.AddTaskAsync(casa.Id, "Barrer");
            t.IsPinned = true;
            await d.Repo.UpdateTaskAsync(t);
            await d.Repo.AddStepsAsync(t.Id, ["a", "b"]);
            await d.Repo.AddTaskAsync(compra.Id, "Leche");
            var hecha = await d.Repo.AddTaskAsync(casa.Id, "Hecha");
            await d.Tasks.CompleteTaskAsync(hecha);
        });

        Assert.True(panel.IsVisible);
        Assert.Equal(2, panel.TaskList.Items.Count);
        Assert.Equal(Localization.Loc.Format("PendingMany", 2), panel.StatusLabel.Text);
        Assert.Equal(2, pendientes.Last());
        Assert.Equal("Ctrl+Alt+T", panel.HotkeyLabel.Text);

        var barrer = panel.TaskList.Items.Cast<FlyoutWindow.TaskRow>().Single(r => r.Title.Contains("Barrer"));
        Assert.Equal("📌 Barrer", barrer.Title);
        Assert.Equal("Casa", barrer.ListName);
        Assert.Equal(Visibility.Visible, barrer.StepsVisibility);
        var leche = panel.TaskList.Items.Cast<FlyoutWindow.TaskRow>().Single(r => r.Title == "Leche");
        Assert.Equal("Familia · Compra", leche.ListName);
        Assert.Equal(Visibility.Collapsed, leche.StepsVisibility);

        // El desplegable de listas trae las dos, y se conserva la elegida al recargar.
        Assert.Equal(2, ((IEnumerable<object>)panel.ListPicker.ItemsSource).Count());
        var segunda = ((IEnumerable<object>)panel.ListPicker.ItemsSource).Last();
        panel.ListPicker.SelectedItem = segunda;
        var elegida = panel.ListPicker.SelectedValue;
        await panel.ReloadAsync();
        Assert.Equal(elegida, panel.ListPicker.SelectedValue);

        // Completar una: celebra y baja la cuenta. Desmarcar una pendiente no hace nada.
        await Ui.Llamar(panel, "OnTaskChecked", new CheckBox { Tag = barrer.Id }, new RoutedEventArgs());
        Assert.True((await datos.Repo.GetTaskAsync(barrer.Id))!.IsDone);
        Assert.Equal(Localization.Loc.Get("PendingOne"), panel.StatusLabel.Text);
        Assert.NotEmpty(panel.XpToastLabel.Text);
        Assert.NotEmpty(panel.Confetti.Children);

        await Ui.Llamar(panel, "OnTaskUnchecked", new CheckBox { Tag = leche.Id }, new RoutedEventArgs());
        Assert.False((await datos.Repo.GetTaskAsync(leche.Id))!.IsDone);

        // Desmarcar una hecha la devuelve.
        await Ui.Llamar(panel, "OnTaskUnchecked", new CheckBox { Tag = barrer.Id }, new RoutedEventArgs());
        Assert.False((await datos.Repo.GetTaskAsync(barrer.Id))!.IsDone);

        // Casillas sin tarea, o de una que ya esta hecha o no existe, no hacen nada.
        await Ui.Llamar(panel, "OnTaskChecked", new CheckBox(), new RoutedEventArgs());
        await Ui.Llamar(panel, "OnTaskUnchecked", new CheckBox(), new RoutedEventArgs());
        await Ui.Llamar(panel, "OnTaskChecked", new CheckBox { Tag = Guid.NewGuid() }, new RoutedEventArgs());

        // Todo hecho.
        foreach (var t in await datos.Repo.GetAllTasksAsync(TaskFilter.Pending))
        {
            await datos.Tasks.CompleteTaskAsync(t);
        }

        await panel.ReloadAsync();
        Assert.Equal(Localization.Loc.Get("NothingInMyDay"), panel.StatusLabel.Text);
        Assert.Equal(0, pendientes.Last());
    });

    [Fact]
    public Task Escribir_una_tarea_la_crea_en_la_lista_elegida_y_abre_el_detalle() => Ui.Run(async () =>
    {
        var (datos, panel, _) = await AbrirAsync(async d => await d.Repo.CreateListAsync("Casa"));

        // Sin texto, nada.
        await Ui.Tecla(panel.QuickAdd, Key.Enter);
        await Ui.Tecla(panel.QuickAdd, Key.Down);

        Ui.ResponderAsync(async w =>
        {
            await Ui.Calma();
            ((TaskDetailWindow)w).TitleBox.Text = "Tender la ropa";
            await Ui.Pulsar(((TaskDetailWindow)w).SaveButton);
        });
        panel.QuickAdd.Text = "Tender";
        await Ui.Tecla(panel.QuickAdd, Key.Enter);

        Assert.Equal(["Tender la ropa"], (await datos.Repo.GetAllTasksAsync(TaskFilter.All)).Select(t => t.Title));
        Assert.False(panel.IsVisible);
        Assert.Equal(string.Empty, panel.QuickAdd.Text);

        // Con el boton, otra; el detalle se cierra sin cambios.
        Ui.Responder(Dialogo.Cerrar);
        await Ui.Hacer(panel.ShowFlyout);
        panel.QuickAdd.Text = "Planchar";
        await Ui.Llamar(panel, "OnAddClick", null, new RoutedEventArgs());
        Assert.Equal(2, (await datos.Repo.GetAllTasksAsync(TaskFilter.All)).Count);

        // Abrir por el lapiz y por doble clic.
        await Ui.Hacer(panel.ShowFlyout);
        var fila = panel.TaskList.Items.Cast<FlyoutWindow.TaskRow>().First();
        Ui.Responder(Dialogo.Cerrar, Dialogo.Cerrar);
        await Ui.Llamar(panel, "OnOpenTaskClick", new Button { Tag = fila.Id }, new RoutedEventArgs());
        await Ui.Hacer(panel.ShowFlyout);
        var item = await Ui.Fila(panel.TaskList, panel.TaskList.Items[0]!);
        await Ui.Llamar(panel, "OnTaskDoubleClick", panel.TaskList, Ui.Raton(Ui.DentroDe(item)));
        await Ui.Llamar(panel, "OnTaskDoubleClick", panel.TaskList, Ui.Raton(panel.TaskList));
        await Ui.Llamar(panel, "OnOpenTaskClick", new Button { Tag = Guid.NewGuid() }, new RoutedEventArgs());
        await Ui.Llamar(panel, "OnOpenTaskClick", new Button(), new RoutedEventArgs());
        Assert.Equal(4, Ui.Abiertas.Count(w => w is TaskDetailWindow));
    });

    [Fact]
    public Task Buscar_y_acotar_por_etiqueta() => Ui.Run(async () =>
    {
        var (datos, panel, _) = await AbrirAsync(async d =>
        {
            var casa = await d.Repo.CreateListAsync("Casa");
            var a = await d.Repo.AddTaskAsync(casa.Id, "Pintar");
            a.Tags = "obra";
            await d.Repo.UpdateTaskAsync(a);
            await d.Repo.AddTaskAsync(casa.Id, "Comer");
        });

        var chips = panel.TagFilterBox.Children.OfType<ToggleButton>().ToList();
        Assert.Equal(3, chips.Count);
        Assert.Equal(Visibility.Visible, panel.TagFilterScroll.Visibility);

        await Ui.Pulsar(chips[2]);
        Assert.Equal(["Pintar"], Titulos(panel));
        Assert.Equal("obra", datos.Settings.FlyoutTag);
        await Ui.Pulsar(panel.TagFilterBox.Children.OfType<ToggleButton>().ElementAt(1));
        Assert.Equal(["Comer"], Titulos(panel));
        await Ui.Pulsar(panel.TagFilterBox.Children.OfType<ToggleButton>().ElementAt(0));

        await Ui.Escribir(panel.SearchBox, "pin");
        Assert.Equal(["Pintar"], Titulos(panel));
        await Ui.Hacer(() => Ui.Invocar(panel, "OnClearSearchClick", null, new RoutedEventArgs()));
        Assert.Equal(2, panel.TaskList.Items.Count);

        // Ctrl+clic: las dos a la vez, y se guardan juntas.
        var antes = KeyboardModifiers.Ctrl;
        try
        {
            KeyboardModifiers.Ctrl = () => true;
            await Ui.Pulsar(panel.TagFilterBox.Children.OfType<ToggleButton>().ElementAt(2));
            await Ui.Pulsar(panel.TagFilterBox.Children.OfType<ToggleButton>().ElementAt(1));
            Assert.Equal(["Comer", "Pintar"], Titulos(panel).Order());
            Assert.Equal($"obra,{TaskManager.Core.Data.TaskRepository.NoTag}", datos.Settings.FlyoutTag);
        }
        finally
        {
            KeyboardModifiers.Ctrl = antes;
        }
        await Ui.Pulsar(panel.TagFilterBox.Children.OfType<ToggleButton>().ElementAt(0));

        // Si la etiqueta guardada ya no tiene nada pendiente, se suelta.
        await datos.Settings.SetFlyoutTagAsync("obra");
        var otro = new FlyoutWindow(datos.Tasks, datos.Settings);
        await Ui.Hacer(otro.ShowFlyout);
        Assert.Equal(["Pintar"], Titulos(otro));
        await datos.Tasks.CompleteTaskAsync((await datos.Repo.GetAllTasksAsync(TaskFilter.All)).Single(t => t.Title == "Pintar"));
        await otro.ReloadAsync();
        Assert.Equal(["Comer"], Titulos(otro));
        otro.CloseForReal();

        // Sin ninguna etiqueta viva, la fila se esconde.
        await datos.Tasks.CompleteTaskAsync((await datos.Repo.GetAllTasksAsync(TaskFilter.All)).Single(t => t.Title == "Comer"));
        await panel.ReloadAsync();
        Assert.Equal(Visibility.Collapsed, panel.TagFilterScroll.Visibility);
        Assert.Equal(Localization.Loc.Get("NothingInMyDay"), panel.StatusLabel.Text);
    });

    [Fact]
    public Task Se_esconde_en_vez_de_cerrarse_y_avisa_de_los_botones() => Ui.Run(async () =>
    {
        var (_, panel, _) = await AbrirAsync();
        var pedidos = new List<string>();
        panel.SettingsRequested += (_, _) => pedidos.Add("ajustes");
        panel.CalendarRequested += (_, _) => pedidos.Add("calendario");
        panel.MainRequested += (_, _) => pedidos.Add("principal");
        panel.AboutRequested += (_, _) => pedidos.Add("acerca");

        await Ui.Pulsar(panel.SettingsButton);
        await Ui.Pulsar(panel.CalendarButton);
        await Ui.Pulsar(panel.MainButton);
        await Ui.Pulsar(panel.AboutButton);
        Assert.Equal(["ajustes", "calendario", "principal", "acerca"], pedidos);
        Assert.False(panel.IsVisible);

        // Escape lo esconde; otra tecla no.
        await Ui.Hacer(panel.ShowFlyout);
        await Ui.Tecla(panel, Key.A);
        Assert.True(panel.IsVisible);
        await Ui.Tecla(panel, Key.Escape);
        Assert.False(panel.IsVisible);

        // Perder el foco lo esconde.
        await Ui.Hacer(panel.ShowFlyout);
        Ui.Invocar(panel, "OnDeactivated", EventArgs.Empty);
        Assert.False(panel.IsVisible);
        Ui.Invocar(panel, "OnDeactivated", EventArgs.Empty);

        // La X lo esconde; CloseForReal lo cierra.
        await Ui.Hacer(panel.ShowFlyout);
        panel.Close();
        Assert.False(panel.IsVisible);
        Assert.Contains(panel, Ui.App.Windows.OfType<FlyoutWindow>());
        panel.CloseForReal();
        Assert.DoesNotContain(panel, Ui.App.Windows.OfType<FlyoutWindow>());
    });

    [Fact]
    public Task Refrescar_espera_a_la_sincronizacion() => Ui.Run(async () =>
    {
        var datos = await Ui.Datos();
        var sync = new SyncFalso();
        var auth = new SupabaseAuthService(new HttpClient(new TaskManager.Tests.FakeHttp()), datos.Settings,
            new TaskManager.Tests.FakeTokens(), new TaskManager.Tests.FakeBrowser());
        await auth.SignInLocallyAsync();
        using var coordinador = new SyncCoordinator(sync, auth, datos.Repo, datos.Settings);
        var panel = new FlyoutWindow(datos.Tasks, datos.Settings, coordinador);
        await Ui.Hacer(panel.ShowFlyout);

        await Ui.Pulsar(panel.RefreshButton);
        Assert.Contains("start", sync.Llamadas);

        typeof(FlyoutWindow).GetField("_refreshing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(panel, true);
        sync.Llamadas.Clear();
        await Ui.Pulsar(panel.RefreshButton);
        Assert.Empty(sync.Llamadas);

        // Sin coordinador solo relee.
        var solo = new FlyoutWindow(datos.Tasks, datos.Settings);
        await Ui.Hacer(solo.ShowFlyout);
        await Ui.Pulsar(solo.RefreshButton);
        Assert.Equal(Localization.Loc.Get("NothingInMyDay"), solo.StatusLabel.Text);
    });

    [Fact]
    public Task Arrastrar_para_ordenar_en_el_panel() => Ui.Run(async () =>
    {
        var (datos, panel, _) = await AbrirAsync(async d =>
        {
            var casa = await d.Repo.CreateListAsync("Casa");
            foreach (var t in new[] { "A", "B", "C" })
            {
                await d.Repo.AddTaskAsync(casa.Id, t);
            }
        });

        var filas = panel.TaskList.Items.Cast<FlyoutWindow.TaskRow>().ToList();
        var (a, b, c) = (filas[0].Title, filas[1].Title, filas[2].Title);
        var primera = await Ui.Fila(panel.TaskList, filas[0]);

        Ui.Invocar(panel, "OnTaskDragStart", panel.TaskList, Ui.Raton(Ui.DentroDe(primera), UIElement.PreviewMouseLeftButtonDownEvent));
        var mover = new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent };
        Ui.Invocar(panel, "OnTaskDragMove", panel.TaskList, mover);
        Ui.Sistema.Boton = MouseButtonState.Pressed;
        Ui.Sistema.Posicion = new Point(0, 50);
        Ui.Invocar(panel, "OnTaskDragMove", panel.TaskList, mover);
        Assert.Same(filas[0], Assert.Single(Ui.Sistema.Arrastres).Datos);

        var encima = Ui.Soltar(filas[0], panel.TaskList);
        Ui.Invocar(panel, "OnTaskDragOver", panel.TaskList, encima);
        Assert.Equal(DragDropEffects.Move, encima.Effects);

        // Al hueco de abajo: al final.
        await Ui.Llamar(panel, "OnTaskDrop", panel.TaskList, Ui.Soltar(filas[0], panel.TaskList));
        Assert.Equal([b, c, a], Titulos(panel));
        await panel.ReloadAsync();
        Assert.Equal([b, c, a], Titulos(panel));

        // Sobre si misma o algo ajeno, nada.
        var ultima = panel.TaskList.Items.Cast<FlyoutWindow.TaskRow>().Last();
        await Ui.Llamar(panel, "OnTaskDrop", panel.TaskList, Ui.Soltar(ultima, panel.TaskList));
        await Ui.Llamar(panel, "OnTaskDrop", panel.TaskList, Ui.Soltar("x", panel.TaskList));
        Assert.Equal([b, c, a], Titulos(panel));

        // Sin fila arrastrada, mover no hace nada.
        Ui.Invocar(panel, "OnTaskDragMove", panel.TaskList, mover);
        Assert.Single(Ui.Sistema.Arrastres);
    });

    [Fact]
    public Task Subir_de_nivel_y_desbloquear_salen_en_el_aviso() => Ui.Run(async () =>
    {
        var (_, panel, _) = await AbrirAsync();
        Ui.Invocar(panel, "Celebrate", new TaskManager.Core.Gamification.Celebration(30, 2.0, 130, 2, true, null));
        Assert.Equal(Localization.Loc.Format("LevelUp", 2, 30), panel.XpToastLabel.Text);

        Ui.Invocar(panel, "Celebrate", new TaskManager.Core.Gamification.Celebration(10, 1.5, 140, 2, false, null));
        Assert.StartsWith("+10 XP", panel.XpToastLabel.Text);
        Assert.Contains($"x{1.5:0.#}", panel.XpToastLabel.Text);

        var cosa = new TaskManager.Core.Gamification.Unlockable(2, "tema", "Tema oscuro");
        Ui.Invocar(panel, "Celebrate", new TaskManager.Core.Gamification.Celebration(10, 1.0, 150, 2, false, cosa));
        Assert.Equal(Localization.Loc.Format("UnlockedItem", cosa.Name), panel.XpToastLabel.Text);
    });
}
