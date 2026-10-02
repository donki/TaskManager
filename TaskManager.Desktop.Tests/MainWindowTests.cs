using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Desktop.Services;
using TaskManager.Desktop.Tests.Banco;

namespace TaskManager.Desktop.Tests;

/// <summary>La ventana principal: «Mis tareas», «Mis listas», el tablero y los grupos.</summary>
public class MainWindowTests
{
    internal static async Task<(Datos Datos, MainWindow Ventana)> AbrirAsync(
        Func<Datos, Task>? preparar = null, ISyncService? sync = null, SyncCoordinator? syncing = null)
    {
        var datos = await Ui.Datos();
        if (preparar is not null)
        {
            await preparar(datos);
        }

        var ventana = await Ui.Mostrar(new MainWindow(datos.Tasks, datos.Settings, syncing, sync));
        await Ui.Hasta(() => ventana.FooterLabel.Text.Length > 0 && ventana.KanbanFooter.Text.Length > 0, que: "la primera carga");
        await Ui.Calma(5);
        return (datos, ventana);
    }

    internal static T P<T>(object fila, string propiedad) => (T)fila.GetType().GetProperty(propiedad)!.GetValue(fila)!;

    private static string Titulo(object fila) => P<string>(fila, "Title");

    private static List<string> Titulos(ItemsControl lista) => [.. lista.Items.Cast<object>().Select(Titulo)];

    private static Button ConTag(object tag) => new() { Tag = tag };

    // ---------------------------------------------------------------------------------
    // Mis tareas
    // ---------------------------------------------------------------------------------

    [Fact]
    public Task Captura_rapida_crea_la_tarea_en_una_lista_nueva_y_abre_su_detalle() => Ui.Run(async () =>
    {
        var (datos, ventana) = await AbrirAsync();

        // Sin texto no hace nada.
        await Ui.Tecla(ventana.QuickAddBox, Key.Enter);
        await Ui.Tecla(ventana.QuickAddBox, Key.A);
        Assert.Empty(await datos.Repo.GetAllTasksAsync(TaskFilter.All));

        // El detalle se abre solo: se cierra sin tocar nada.
        Ui.Responder(Dialogo.Cerrar);
        ventana.QuickAddBox.Text = "  Comprar pan ";
        await Ui.Tecla(ventana.QuickAddBox, Key.Enter);

        var tarea = Assert.Single(await datos.Repo.GetAllTasksAsync(TaskFilter.All));
        Assert.Equal("Comprar pan", tarea.Title);
        Assert.Equal(string.Empty, ventana.QuickAddBox.Text);
        Assert.Contains(Ui.Abiertas, w => w is TaskDetailWindow);
        Assert.Equal(["Comprar pan"], Titulos(ventana.AllTasksBox));

        // Sin ninguna lista se creo la de siempre, y la segunda va a la misma.
        Ui.Responder(Dialogo.Cerrar);
        ventana.QuickAddBox.Text = "Leche";
        await Ui.Llamar(ventana, "OnQuickAddClick", null, new RoutedEventArgs());
        var listas = await datos.Repo.GetPrivateListsAsync();
        Assert.Single(listas);
        Assert.Equal(2, (await datos.Repo.GetTasksAsync(listas[0].Id)).Count);
        Assert.Equal(Localization.Loc.Format("TaskCount", 2), ventana.SummaryLabel.Text);
    });

    [Fact]
    public Task Las_pastillas_de_filtro_cambian_lo_que_se_ve_y_se_recuerdan() => Ui.Run(async () =>
    {
        var (datos, ventana) = await AbrirAsync(async d =>
        {
            var lista = await d.Repo.CreateListAsync("Casa");
            var hecha = await d.Repo.AddTaskAsync(lista.Id, "Hecha");
            await d.Tasks.CompleteTaskAsync(hecha);
            await d.Repo.AddTaskAsync(lista.Id, "Pendiente");
        });

        // Se arranca en pendientes.
        Assert.Equal(["Pendiente"], Titulos(ventana.AllTasksBox));

        var chips = ventana.FilterBox.Children.OfType<ToggleButton>().ToList();
        Assert.Equal(TaskFilters.All.Length, chips.Count);
        var todas = chips.Single(c => (TaskFilter)c.Tag == TaskFilter.All);
        var pendientes = chips.Single(c => (TaskFilter)c.Tag == TaskFilter.Pending);
        Assert.True(pendientes.IsChecked);

        await Ui.Marcar(todas, true);
        Assert.Equal(2, ventana.AllTasksBox.Items.Count);
        Assert.False(pendientes.IsChecked);
        Assert.Equal(TaskFilter.All, datos.Settings.TaskFilter);
        Assert.Equal("2 tareas", ventana.SummaryLabel.Text);

        // Volver a pulsar el que esta puesto no deja la fila sin ninguno.
        await Ui.Marcar(todas, false);
        Assert.True(todas.IsChecked);

        // Y la etiqueta del filtro dice cual es.
        Assert.Equal(Localization.Loc.Get(TaskFilters.KeyOf(TaskFilter.All)), ventana.FilterLabel.Text);
    });

    [Fact]
    public Task Las_etiquetas_filtran_y_se_borran_con_el_boton_derecho() => Ui.Run(async () =>
    {
        var (datos, ventana) = await AbrirAsync(async d =>
        {
            var lista = await d.Repo.CreateListAsync("Casa");
            var a = await d.Repo.AddTaskAsync(lista.Id, "Con etiqueta");
            a.Tags = "casa";
            await d.Repo.UpdateTaskAsync(a);
            var b = await d.Repo.AddTaskAsync(lista.Id, "Hecha con etiqueta");
            b.Tags = "vieja";
            await d.Repo.UpdateTaskAsync(b);
            await d.Tasks.CompleteTaskAsync(b);
            await d.Repo.AddTaskAsync(lista.Id, "Sin etiqueta");
        });

        Assert.Equal(Visibility.Visible, ventana.TagFilterRow.Visibility);
        var chips = ventana.TagFilterBox.Children.OfType<ToggleButton>().ToList();
        Assert.Equal(["#casa"], chips.Skip(2).Select(c => (string)c.Content));

        // #casa: solo la suya, y se recuerda.
        await Ui.Pulsar(chips[2]);
        Assert.Equal(["Con etiqueta"], Titulos(ventana.AllTasksBox));
        Assert.Equal("casa", datos.Settings.TaskTag);
        Assert.EndsWith("#casa", ventana.FilterLabel.Text);

        // Sin etiqueta.
        await Ui.Pulsar(ventana.TagFilterBox.Children.OfType<ToggleButton>().ElementAt(1));
        Assert.Equal(["Sin etiqueta"], Titulos(ventana.AllTasksBox));
        Assert.EndsWith(Localization.Loc.Get("NoTagFilter"), ventana.FilterLabel.Text);

        // Volver a #casa y borrarla desde su menu: se pregunta (tiene pendientes) y se cancela.
        await Ui.Pulsar(ventana.TagFilterBox.Children.OfType<ToggleButton>().ElementAt(2));
        var menu = ventana.TagFilterBox.Children.OfType<ToggleButton>().ElementAt(2).ContextMenu!;
        var borrar = (MenuItem)menu.Items[0]!;
        Ui.Responder(Dialogo.Primero);
        await Ui.Hacer(() => borrar.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
        Assert.Equal("casa", (await datos.Repo.GetAllTasksAsync(TaskFilter.All)).Single(t => t.Title == "Con etiqueta").Tags);

        // Ahora si: se borra, se suelta el filtro y se avisa.
        Ui.Responder(Dialogo.Aceptar, Dialogo.Aceptar);
        await Ui.Hacer(() => borrar.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
        Assert.Equal(string.Empty, (await datos.Repo.GetAllTasksAsync(TaskFilter.All)).Single(t => t.Title == "Con etiqueta").Tags);
        Assert.Null(datos.Settings.TaskTag);
        Assert.Contains(Dialogo.Leidos.Last(), t => t.Contains("casa"));

        // Queda la etiqueta de una tarea hecha: la fila sigue para poder borrarla, pero sin pastillas.
        Assert.Equal(Visibility.Visible, ventana.TagFilterRow.Visibility);
        Assert.Empty(ventana.TagFilterBox.Children);
        Assert.Equal(Localization.Loc.Get(TaskFilters.KeyOf(TaskFilter.Pending)), ventana.FilterLabel.Text);

        // Borrar la de la tarea hecha (la otra pregunta) desde la ventana de etiquetas.
        Ui.ResponderAsync(async w =>
        {
            await Ui.Hasta(() => Ui.Textos(w).Contains("#vieja"));
            Ui.Responder(Dialogo.Aceptar);
            var papelera = Ui.Botones(w).First(b => (string?)b.ToolTip == Localization.Loc.Get("DeleteTag"));
            await Ui.Hacer(() => papelera.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));
            await Ui.Hasta(() => Ui.Textos(w).Contains(Localization.Loc.Get("TagsNone")) &&
                                 Ui.Buscar<TextBlock>(w).Any(t => t.Text == Localization.Loc.Get("TagsNone") && t.Visibility == Visibility.Visible));
            Dialogo.Primero(w);
        });
        await Ui.Llamar(ventana, "OnTagsClick", null, new RoutedEventArgs());
        Assert.Equal(Visibility.Collapsed, ventana.TagFilterRow.Visibility);
        Assert.Empty(await datos.Repo.GetTagsAsync());
    });

    [Fact]
    public Task Borrar_una_etiqueta_que_solo_llevan_tareas_hechas_se_confirma_sin_contar_pendientes() => Ui.Run(async () =>
    {
        var (datos, ventana) = await AbrirAsync(async d =>
        {
            var lista = await d.Repo.CreateListAsync("Casa");
            var a = await d.Repo.AddTaskAsync(lista.Id, "Viva");
            a.Tags = "x";
            await d.Repo.UpdateTaskAsync(a);
        });

        var chip = ventana.TagFilterBox.Children.OfType<ToggleButton>().ElementAt(2);
        await d_CompletarTodo(datos);

        // La pastilla sigue pintada de antes: su menu pregunta con el mensaje de «solo hechas».
        Ui.Responder(Dialogo.Aceptar, Dialogo.Aceptar);
        await Ui.Hacer(() => ((MenuItem)chip.ContextMenu!.Items[0]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
        Assert.Contains(Dialogo.Leidos[^2], t => t == Localization.Loc.Format("DeleteTagDone", "x", 1));
        Assert.Empty(await datos.Repo.GetTagsAsync());

        static async Task d_CompletarTodo(Datos d)
        {
            foreach (var t in await d.Repo.GetAllTasksAsync(TaskFilter.All))
            {
                await d.Tasks.CompleteTaskAsync(t);
            }
        }
    });

    [Fact]
    public Task Buscar_filtra_al_escribir_y_dice_cuando_no_hay_nada() => Ui.Run(async () =>
    {
        var (_, ventana) = await AbrirAsync(async d =>
        {
            var lista = await d.Repo.CreateListAsync("Casa");
            await d.Repo.AddTaskAsync(lista.Id, "Llamar al fontanero");
            await d.Repo.AddTaskAsync(lista.Id, "Comprar pan");
        });

        await Ui.Escribir(ventana.SearchBox, "fonta");
        Assert.Equal(["Llamar al fontanero"], Titulos(ventana.AllTasksBox));
        Assert.Equal(Visibility.Collapsed, ventana.NoTasksLabel.Visibility);

        await Ui.Escribir(ventana.SearchBox, "zzz");
        Assert.Empty(ventana.AllTasksBox.Items);
        Assert.Equal(Visibility.Visible, ventana.NoTasksLabel.Visibility);
        Assert.Equal(Localization.Loc.Format("NoSearchResults", "zzz"), ventana.NoTasksLabel.Text);

        await Ui.Hacer(() => Ui.Invocar(ventana, "OnClearSearchClick", null, new RoutedEventArgs()));
        Assert.Equal(2, ventana.AllTasksBox.Items.Count);
        Assert.Equal(Localization.Loc.Get("NoTasksForFilter"), ventana.NoTasksLabel.Text);
    });

    [Fact]
    public Task Marcar_la_casilla_completa_y_desmarcarla_la_devuelve() => Ui.Run(async () =>
    {
        TaskItem tarea = null!;
        var (datos, ventana) = await AbrirAsync(async d =>
        {
            var lista = await d.Repo.CreateListAsync("Casa");
            tarea = await d.Repo.AddTaskAsync(lista.Id, "Tarea");
        });

        await Ui.Llamar(ventana, "OnTaskToggled", new CheckBox { Tag = tarea.Id }, new RoutedEventArgs());
        Assert.True((await datos.Repo.GetTaskAsync(tarea.Id))!.IsDone);
        Assert.Empty(ventana.AllTasksBox.Items);

        await Ui.Llamar(ventana, "OnTaskToggled", new CheckBox { Tag = tarea.Id }, new RoutedEventArgs());
        Assert.False((await datos.Repo.GetTaskAsync(tarea.Id))!.IsDone);

        // Ni una casilla sin tarea ni una tarea que ya no existe hacen nada.
        await Ui.Llamar(ventana, "OnTaskToggled", new CheckBox(), new RoutedEventArgs());
        await Ui.Llamar(ventana, "OnTaskToggled", new CheckBox { Tag = Guid.NewGuid() }, new RoutedEventArgs());
    });

    [Fact]
    public Task Abrir_una_tarea_por_el_lapiz_o_doble_clic_y_releer_si_cambio() => Ui.Run(async () =>
    {
        TaskItem tarea = null!;
        var (datos, ventana) = await AbrirAsync(async d =>
        {
            var lista = await d.Repo.CreateListAsync("Casa");
            tarea = await d.Repo.AddTaskAsync(lista.Id, "Original");
        });

        // Por el lapiz: se cambia el titulo y se guarda.
        Ui.ResponderAsync(async w =>
        {
            var detalle = (TaskDetailWindow)w;
            await Ui.Calma();
            detalle.TitleBox.Text = "Cambiada";
            await Ui.Pulsar(detalle.SaveButton);
        });
        await Ui.Llamar(ventana, "OnOpenTaskClick", ConTag(tarea.Id), new RoutedEventArgs());
        Assert.Equal(["Cambiada"], Titulos(ventana.AllTasksBox));

        // Por doble clic sobre la fila.
        Ui.Responder(Dialogo.Cerrar);
        var fila = await Ui.Fila(ventana.AllTasksBox, ventana.AllTasksBox.Items[0]!);
        await Ui.Llamar(ventana, "OnRowDoubleClick", ventana.AllTasksBox, Ui.Raton(Ui.DentroDe(fila)));
        Assert.Equal(2, Ui.Abiertas.Count(w => w is TaskDetailWindow));

        // Fuera de una fila, o con una tarea que ya no esta, no se abre nada.
        await Ui.Llamar(ventana, "OnRowDoubleClick", ventana.AllTasksBox, Ui.Raton(ventana.AllTasksBox));
        await Ui.Llamar(ventana, "OnOpenTaskClick", ConTag(Guid.NewGuid()), new RoutedEventArgs());
        await Ui.Llamar(ventana, "OnOpenTaskClick", new Button(), new RoutedEventArgs());
        Assert.Equal(2, Ui.Abiertas.Count(w => w is TaskDetailWindow));
    });

    [Fact]
    public Task Refrescar_sincroniza_y_relee_sin_pedirlo_dos_veces() => Ui.Run(async () =>
    {
        var datos0 = await Ui.Datos();
        var sync = new SyncFalso();
        var auth = new SupabaseAuthService(new HttpClient(new TaskManager.Tests.FakeHttp()), datos0.Settings,
            new TaskManager.Tests.FakeTokens(), new TaskManager.Tests.FakeBrowser());
        await auth.SignInLocallyAsync();
        using var coordinador = new SyncCoordinator(sync, auth, datos0.Repo, datos0.Settings);

        var ventana = await Ui.Mostrar(new MainWindow(datos0.Tasks, datos0.Settings, coordinador, sync));
        await Ui.Hasta(() => ventana.FooterLabel.Text.Length > 0);
        var lista = await datos0.Repo.CreateListAsync("Nueva desde el movil");

        await Ui.Pulsar(ventana.RefreshButton);
        Assert.Contains("start", sync.Llamadas);
        Assert.Contains(ventana.ListsBox.Items.Cast<object>(), l => P<string>(l, "Name") == lista.Name);
        Assert.False(Ui.Campo<bool>(ventana, "_refreshing"));

        // Con uno en marcha, el segundo no hace nada.
        typeof(MainWindow).GetField("_refreshing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(ventana, true);
        sync.Llamadas.Clear();
        await Ui.Pulsar(ventana.RefreshButton);
        Assert.Empty(sync.Llamadas);
    });

    // ---------------------------------------------------------------------------------
    // Mis listas
    // ---------------------------------------------------------------------------------

    [Fact]
    public Task Crear_renombrar_y_trabajar_en_una_lista() => Ui.Run(async () =>
    {
        var (datos, ventana) = await AbrirAsync();
        Assert.Empty(ventana.ListsBox.Items);
        Assert.Equal(string.Empty, ventana.ListFooterLabel.Text);

        // Nueva: el nombre se pide; cancelar no crea nada.
        Ui.Responder(Dialogo.Cerrar);
        await Ui.Llamar(ventana, "OnNewListClick", null, new RoutedEventArgs());
        Assert.Empty(await datos.Repo.GetPrivateListsAsync());

        Ui.Responder(Dialogo.Escribir("Trabajo"));
        await Ui.Llamar(ventana, "OnNewListClick", null, new RoutedEventArgs());
        var lista = Assert.Single(await datos.Repo.GetPrivateListsAsync());
        Assert.Equal("Trabajo", lista.Name);
        Assert.Same(ventana.ListsBox.SelectedItem, ventana.ListsBox.Items[0]);

        // Añadir con Enter: se crea en la lista elegida y se abre el detalle.
        Ui.Responder(Dialogo.Cerrar);
        ventana.NewTaskBox.Text = "Informe";
        await Ui.Tecla(ventana.NewTaskBox, Key.Enter);
        Assert.Equal(["Informe"], Titulos(ventana.ListTasksBox));
        Assert.Equal(Localization.Loc.Get("OnePending"), P<string>(ventana.ListsBox.Items[0]!, "Caption"));

        // Con el boton, otra; sin texto, nada.
        Ui.Responder(Dialogo.Cerrar);
        ventana.NewTaskBox.Text = "Correo";
        await Ui.Llamar(ventana, "OnAddTaskClick", null, new RoutedEventArgs());
        await Ui.Tecla(ventana.NewTaskBox, Key.Enter);
        await Ui.Tecla(ventana.NewTaskBox, Key.B);
        Assert.Equal(2, ventana.ListTasksBox.Items.Count);
        Assert.Equal(Localization.Loc.Format("ManyPending", 2), P<string>(ventana.ListsBox.Items[0]!, "Caption"));

        // Buscar dentro de la lista.
        await Ui.Escribir(ventana.ListSearchBox, "corr");
        Assert.Equal(["Correo"], Titulos(ventana.ListTasksBox));
        await Ui.Hacer(() => Ui.Invocar(ventana, "OnClearListSearchClick", null, new RoutedEventArgs()));
        Assert.Equal(2, ventana.ListTasksBox.Items.Count);

        // Renombrar por el lapiz: el mismo nombre o vacio no cambian nada.
        Ui.Responder(Dialogo.Escribir("Trabajo"), Dialogo.Escribir("  "), Dialogo.Escribir("Oficina"));
        await Ui.Llamar(ventana, "OnRenameListClick", ConTag(lista.Id), new RoutedEventArgs());
        await Ui.Llamar(ventana, "OnRenameListClick", ConTag(lista.Id), new RoutedEventArgs());
        Assert.Equal("Trabajo", (await datos.Repo.GetListAsync(lista.Id))!.Name);
        await Ui.Llamar(ventana, "OnRenameListClick", ConTag(lista.Id), new RoutedEventArgs());
        Assert.Equal("Oficina", (await datos.Repo.GetListAsync(lista.Id))!.Name);
        Assert.Equal("Oficina", P<string>(ventana.ListsBox.Items[0]!, "Name"));

        // Y por doble clic sobre la lista. Una que ya no existe no pide nada.
        Ui.Responder(Dialogo.Escribir("Despacho"));
        await Ui.Llamar(ventana, "OnListDoubleClick", null, Ui.Raton(ventana.ListsBox));
        Assert.Equal("Despacho", (await datos.Repo.GetListAsync(lista.Id))!.Name);
        await Ui.Llamar(ventana, "OnRenameListClick", ConTag(Guid.NewGuid()), new RoutedEventArgs());
        await Ui.Llamar(ventana, "OnRenameListClick", new Button(), new RoutedEventArgs());

        // Elegir otra lista enseña sus tareas.
        var otra = await datos.Repo.CreateListAsync("Casa");
        await ventana.ReloadAsync();
        var filaOtra = ventana.ListsBox.Items.Cast<object>().Single(l => P<Guid>(l, "Id") == otra.Id);
        await Ui.Hacer(() => ventana.ListsBox.SelectedItem = filaOtra);
        Assert.Empty(ventana.ListTasksBox.Items);
    });

    [Fact]
    public Task Borrar_una_lista_pregunta_a_donde_van_sus_tareas() => Ui.Run(async () =>
    {
        TaskList casa = null!, trabajo = null!, vacia = null!;
        var (datos, ventana) = await AbrirAsync(async d =>
        {
            casa = await d.Repo.CreateListAsync("Casa");
            trabajo = await d.Repo.CreateListAsync("Trabajo");
            vacia = await d.Repo.CreateListAsync("Vacia");
            await d.Repo.AddTaskAsync(casa.Id, "Barrer");
            await d.Repo.AddTaskAsync(casa.Id, "Fregar");
        });

        // Vacia: se confirma; cancelar no la borra.
        Ui.Responder(Dialogo.Primero, Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnDeleteListClick", ConTag(vacia.Id), new RoutedEventArgs());
        Assert.NotNull(await datos.Repo.GetListAsync(vacia.Id));
        await Ui.Llamar(ventana, "OnDeleteListClick", ConTag(vacia.Id), new RoutedEventArgs());
        Assert.Null(await datos.Repo.GetListAsync(vacia.Id));

        // Con tareas: cerrar sin elegir no hace nada; elegir «Trabajo» las muda.
        Ui.Responder(Dialogo.Cerrar, Dialogo.Elegir(0));
        await Ui.Llamar(ventana, "OnDeleteListClick", ConTag(casa.Id), new RoutedEventArgs());
        Assert.NotNull(await datos.Repo.GetListAsync(casa.Id));
        await Ui.Llamar(ventana, "OnDeleteListClick", ConTag(casa.Id), new RoutedEventArgs());
        Assert.Null(await datos.Repo.GetListAsync(casa.Id));
        Assert.Equal(2, (await datos.Repo.GetTasksAsync(trabajo.Id)).Count);
        Assert.Contains(Dialogo.Leidos.Last(), t => t == Localization.Loc.Format("MoveTasksMessage", "Casa", 2));

        // La ultima lista con tareas: no hay a donde mandarlas, se pregunta si borrarla con ellas.
        Ui.Responder(Dialogo.Primero, Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnDeleteListClick", ConTag(trabajo.Id), new RoutedEventArgs());
        Assert.NotNull(await datos.Repo.GetListAsync(trabajo.Id));
        await Ui.Llamar(ventana, "OnDeleteListClick", ConTag(trabajo.Id), new RoutedEventArgs());
        Assert.Null(await datos.Repo.GetListAsync(trabajo.Id));
        Assert.Empty(ventana.ListsBox.Items);
        Assert.Empty(await datos.Repo.GetAllTasksAsync(TaskFilter.All));

        // Sin lista o con una que ya no esta, nada.
        await Ui.Llamar(ventana, "OnDeleteListClick", new Button(), new RoutedEventArgs());
        await Ui.Llamar(ventana, "OnDeleteListClick", ConTag(Guid.NewGuid()), new RoutedEventArgs());
    });

    [Fact]
    public Task Borrar_una_lista_con_una_sola_tarea_y_llevarsela_por_delante() => Ui.Run(async () =>
    {
        TaskList casa = null!;
        var (datos, ventana) = await AbrirAsync(async d =>
        {
            casa = await d.Repo.CreateListAsync("Casa");
            await d.Repo.CreateListAsync("Otra");
            await d.Repo.AddTaskAsync(casa.Id, "Unica");
        });

        // «Borrarlas tambien» es la salida alternativa.
        Ui.Responder(Dialogo.Primero);
        await Ui.Llamar(ventana, "OnDeleteListClick", ConTag(casa.Id), new RoutedEventArgs());
        Assert.Contains(Dialogo.Leidos.Last(), t => t == Localization.Loc.Format("MoveTasksOne", "Casa"));
        Assert.Null(await datos.Repo.GetListAsync(casa.Id));
        Assert.Empty(await datos.Repo.GetAllTasksAsync(TaskFilter.All));
    });

    [Fact]
    public Task Arrastrar_para_reordenar_en_mis_tareas() => Ui.Run(async () =>
    {
        var (datos, ventana) = await AbrirAsync(async d =>
        {
            var lista = await d.Repo.CreateListAsync("Casa");
            foreach (var t in new[] { "Uno", "Dos", "Tres" })
            {
                await d.Repo.AddTaskAsync(lista.Id, t);
            }
        });

        var filas = ventana.AllTasksBox.Items.Cast<object>().ToList();
        var primera = await Ui.Fila(ventana.AllTasksBox, filas[0]);

        // Pulsar sobre la primera y moverse: sin boton, con Ctrl o sin pasar el umbral no arrastra.
        Ui.Sistema.Posicion = new Point(10, 10);
        Ui.Invocar(ventana, "OnTaskDragStart", ventana.AllTasksBox, Ui.Raton(Ui.DentroDe(primera), UIElement.PreviewMouseLeftButtonDownEvent));
        var mover = new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent };
        Ui.Invocar(ventana, "OnTaskDragMove", ventana.AllTasksBox, mover);
        Ui.Sistema.Boton = MouseButtonState.Pressed;
        Ui.Invocar(ventana, "OnTaskDragMove", ventana.AllTasksBox, mover);
        Ui.Sistema.Posicion = new Point(10, 80);
        Ui.Sistema.Teclas = ModifierKeys.Control;
        Ui.Invocar(ventana, "OnTaskDragMove", ventana.AllTasksBox, mover);
        Assert.Empty(Ui.Sistema.Arrastres);

        Ui.Sistema.Teclas = ModifierKeys.None;
        Ui.Invocar(ventana, "OnTaskDragMove", ventana.AllTasksBox, mover);
        Assert.Same(filas[0], Assert.Single(Ui.Sistema.Arrastres).Datos);

        // Pasar por encima acepta filas y nada mas.
        var encima = Ui.Soltar(filas[0], ventana.AllTasksBox);
        encima.RoutedEvent = DragDrop.DragOverEvent;
        Ui.Invocar(ventana, "OnTaskDragOver", ventana.AllTasksBox, encima);
        Assert.Equal(DragDropEffects.Move, encima.Effects);
        var otraCosa = Ui.Soltar("texto", ventana.AllTasksBox);
        Ui.Invocar(ventana, "OnTaskDragOver", ventana.AllTasksBox, otraCosa);
        Assert.Equal(DragDropEffects.None, otraCosa.Effects);

        // Soltar la primera sobre la tercera la deja tercera, y el orden se guarda.
        var (a, b, c) = (Titulo(filas[0]), Titulo(filas[1]), Titulo(filas[2]));
        var tercera = await Ui.Fila(ventana.AllTasksBox, filas[2]);
        await Ui.Llamar(ventana, "OnTaskDrop", ventana.AllTasksBox, Ui.Soltar(filas[0], Ui.DentroDe(tercera)));
        Assert.Equal([b, c, a], Titulos(ventana.AllTasksBox));
        await ventana.ReloadAsync();
        Assert.Equal([b, c, a], Titulos(ventana.AllTasksBox));

        // Soltar sobre si misma, o algo que no es una fila, no hace nada; en el hueco va al final.
        filas = ventana.AllTasksBox.Items.Cast<object>().ToList();
        var ultima = await Ui.Fila(ventana.AllTasksBox, filas[2]);
        await Ui.Llamar(ventana, "OnTaskDrop", ventana.AllTasksBox, Ui.Soltar(filas[2], Ui.DentroDe(ultima)));
        await Ui.Llamar(ventana, "OnTaskDrop", ventana.AllTasksBox, Ui.Soltar("texto", ventana.AllTasksBox));
        await Ui.Llamar(ventana, "OnTaskDrop", ventana.AllTasksBox, Ui.Soltar(filas[0], ventana.AllTasksBox));
        Assert.Equal([c, a, b], Titulos(ventana.AllTasksBox));

        // En la lista de «Mis listas» tambien.
        var deLista = ventana.ListTasksBox.Items.Cast<object>().ToList();
        await Ui.Llamar(ventana, "OnTaskDrop", ventana.ListTasksBox, Ui.Soltar(deLista[0], ventana.ListTasksBox));
        Assert.Equal(Titulo(deLista[0]), Titulos(ventana.ListTasksBox).Last());
    });

    // ---------------------------------------------------------------------------------
    // Varias a la vez
    // ---------------------------------------------------------------------------------

    private static async Task Marcar(MainWindow ventana, ListBox lista, params int[] indices)
    {
        await Ui.Hacer(() =>
        {
            lista.SelectedItems.Clear();
            foreach (var i in indices)
            {
                lista.SelectedItems.Add(lista.Items[i]);
            }
        });
    }

    [Fact]
    public Task Acciones_sobre_varias_tareas_marcadas() => Ui.Run(async () =>
    {
        TaskList casa = null!, otra = null!;
        var (datos, ventana) = await AbrirAsync(async d =>
        {
            casa = await d.Repo.CreateListAsync("Casa");
            otra = await d.Repo.CreateListAsync("Otra");
            foreach (var t in new[] { "Uno", "Dos", "Tres" })
            {
                await d.Repo.AddTaskAsync(casa.Id, t);
            }
        });

        // Sin nada marcado, los botones no hacen nada.
        foreach (var boton in new[] { "OnBulkDoneClick", "OnBulkPendingClick", "OnBulkPinOnClick", "OnBulkTagClick", "OnBulkMoveClick", "OnBulkDeleteClick" })
        {
            await Ui.Llamar(ventana, boton, null, new RoutedEventArgs());
        }

        // Una sola no saca la barra; dos si.
        await Marcar(ventana, ventana.AllTasksBox, 0);
        Assert.Equal(Visibility.Collapsed, ventana.SelectionBar.Visibility);
        await Marcar(ventana, ventana.AllTasksBox, 0, 1);
        Assert.Equal(Visibility.Visible, ventana.SelectionBar.Visibility);
        Assert.Equal(Localization.Loc.Format("SelectionCount", 2), ventana.SelectionLabel.Text);

        // Anclar y desanclar.
        await Ui.Llamar(ventana, "OnBulkPinOnClick", null, new RoutedEventArgs());
        Assert.Equal(2, (await datos.Repo.GetAllTasksAsync(TaskFilter.Pinned)).Count);
        Assert.Contains(Titulos(ventana.AllTasksBox), t => t.StartsWith("📌"));
        await Marcar(ventana, ventana.AllTasksBox, 0, 1);
        await Ui.Llamar(ventana, "OnBulkPinOffClick", null, new RoutedEventArgs());
        Assert.Empty(await datos.Repo.GetAllTasksAsync(TaskFilter.Pinned));

        // Etiquetar: se escribe una nueva; cerrar no pone nada.
        await Marcar(ventana, ventana.AllTasksBox, 0, 1);
        Ui.Responder(Dialogo.Cerrar);
        await Ui.Llamar(ventana, "OnBulkTagClick", null, new RoutedEventArgs());
        Assert.Empty(await datos.Repo.GetTagsAsync());
        await Marcar(ventana, ventana.AllTasksBox, 0, 1);
        Ui.Responder(Dialogo.Escribir("lote"));
        await Ui.Llamar(ventana, "OnBulkTagClick", null, new RoutedEventArgs());
        Assert.Equal(["lote"], await datos.Repo.GetTagsAsync());

        // Mover a otra lista: cerrar no mueve; elegir «Otra» si.
        await Marcar(ventana, ventana.AllTasksBox, 0, 1);
        Ui.Responder(Dialogo.Cerrar);
        await Ui.Llamar(ventana, "OnBulkMoveClick", null, new RoutedEventArgs());
        Assert.Empty(await datos.Repo.GetTasksAsync(otra.Id));
        await Marcar(ventana, ventana.AllTasksBox, 0, 1);
        var indiceOtra = ventana.ListsBox.Items.Cast<object>().ToList().FindIndex(l => P<Guid>(l, "Id") == otra.Id);
        Ui.Responder(Dialogo.Elegir(indiceOtra));
        await Ui.Llamar(ventana, "OnBulkMoveClick", null, new RoutedEventArgs());
        Assert.Equal(2, (await datos.Repo.GetTasksAsync(otra.Id)).Count);

        // Completar y devolver.
        await Marcar(ventana, ventana.AllTasksBox, 0, 1);
        var ids = ventana.AllTasksBox.SelectedItems.Cast<object>().Select(r => P<Guid>(r, "Id")).ToList();
        await Ui.Llamar(ventana, "OnBulkDoneClick", null, new RoutedEventArgs());
        Assert.All(ids, id => Assert.True(datos.Repo.GetTaskAsync(id).Result!.IsDone));

        var todas = ventana.FilterBox.Children.OfType<ToggleButton>().Single(c => (TaskFilter)c.Tag == TaskFilter.All);
        await Ui.Marcar(todas, true);
        var indices = ventana.AllTasksBox.Items.Cast<object>().Select((r, i) => (r, i)).Where(x => ids.Contains(P<Guid>(x.r, "Id"))).Select(x => x.i).ToArray();
        await Marcar(ventana, ventana.AllTasksBox, indices);
        await Ui.Llamar(ventana, "OnBulkPendingClick", null, new RoutedEventArgs());
        Assert.All(ids, id => Assert.False(datos.Repo.GetTaskAsync(id).Result!.IsDone));

        // Marcar en la otra lista suelta lo de esta.
        await Marcar(ventana, ventana.AllTasksBox, 0, 1);
        ventana.ListsBox.SelectedItem = ventana.ListsBox.Items.Cast<object>().Single(l => P<Guid>(l, "Id") == otra.Id);
        await Ui.Calma();
        await Marcar(ventana, ventana.ListTasksBox, 0, 1);
        Assert.Empty(ventana.AllTasksBox.SelectedItems);
        Assert.Equal(Visibility.Visible, ventana.ListSelectionBar.Visibility);
        Ui.Invocar(ventana, "OnClearSelectionClick", null, new RoutedEventArgs());
        Assert.Empty(ventana.ListTasksBox.SelectedItems);
        Ui.Invocar(ventana, "OnTaskSelectionChanged", "no es una lista", null);

        // Borrar: cancelar no borra; aceptar si.
        await Marcar(ventana, ventana.AllTasksBox, 0, 1);
        Ui.Responder(Dialogo.Primero);
        await Ui.Llamar(ventana, "OnBulkDeleteClick", null, new RoutedEventArgs());
        Assert.Equal(3, (await datos.Repo.GetAllTasksAsync(TaskFilter.All)).Count);
        Ui.Responder(Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnBulkDeleteClick", null, new RoutedEventArgs());
        Assert.Single(await datos.Repo.GetAllTasksAsync(TaskFilter.All));
    });

    [Fact]
    public Task Borrar_varias_de_una_serie_pregunta_si_llevarse_la_serie() => Ui.Run(async () =>
    {
        var (datos, ventana) = await AbrirAsync(async d =>
        {
            var lista = await d.Repo.CreateListAsync("Casa");
            var t = await d.Repo.AddTaskAsync(lista.Id, "Regar");
            t.PlannedFor = DateTime.Today;
            t.DueAt = DateTime.Today.AddDays(3);
            t.RecurrenceRule = new Recurrence(RecurrenceKind.Daily, 1, 0, 0, 0).Serialize();
            await d.Repo.UpdateTaskAsync(t);
            await d.Tasks.GenerateSeriesAsync(t);
            await d.Repo.AddTaskAsync(lista.Id, "Suelta");
        });

        var todas = ventana.FilterBox.Children.OfType<ToggleButton>().Single(c => (TaskFilter)c.Tag == TaskFilter.All);
        await Ui.Marcar(todas, true);
        var total = ventana.AllTasksBox.Items.Count;
        Assert.True(total > 3);

        int[] Regar(int cuantas) => [.. ventana.AllTasksBox.Items.Cast<object>().Select((r, i) => (r, i))
            .Where(x => Titulo(x.r) == "Regar").Select(x => x.i).Take(cuantas)];

        // Cancelar la segunda pregunta no borra nada.
        await Marcar(ventana, ventana.AllTasksBox, Regar(2));
        Ui.Responder(Dialogo.Aceptar, Dialogo.Cerrar);
        await Ui.Llamar(ventana, "OnBulkDeleteClick", null, new RoutedEventArgs());
        Assert.Equal(total, (await datos.Repo.GetAllTasksAsync(TaskFilter.All)).Count);
        Assert.Contains(Dialogo.Leidos.Last(), t => t == Localization.Loc.Format("BulkDeleteSeriesQuestion", 2));

        // Solo las elegidas.
        await Marcar(ventana, ventana.AllTasksBox, Regar(2));
        Ui.Responder(Dialogo.Aceptar, Dialogo.Elegir(1));
        await Ui.Llamar(ventana, "OnBulkDeleteClick", null, new RoutedEventArgs());
        Assert.Equal(total - 2, (await datos.Repo.GetAllTasksAsync(TaskFilter.All)).Count);

        // La serie entera.
        await Marcar(ventana, ventana.AllTasksBox, Regar(1));
        Ui.Responder(Dialogo.Aceptar, Dialogo.Elegir(0));
        await Ui.Llamar(ventana, "OnBulkDeleteClick", null, new RoutedEventArgs());
        Assert.Equal(["Suelta"], (await datos.Repo.GetAllTasksAsync(TaskFilter.All)).Select(t => t.Title));
    });

    // ---------------------------------------------------------------------------------
    // Tablero
    // ---------------------------------------------------------------------------------

    [Fact]
    public Task El_tablero_reparte_filtra_y_crea() => Ui.Run(async () =>
    {
        var (datos, ventana) = await AbrirAsync(async d =>
        {
            var lista = await d.Repo.CreateListAsync("Casa");
            await d.Repo.AddTaskAsync(lista.Id, "Por hacer");
            await d.Repo.AddTaskAsync(lista.Id, "En curso", inProgress: true);
            var hecha = await d.Repo.AddTaskAsync(lista.Id, "Hecha");
            hecha.Tags = "fin";
            await d.Repo.UpdateTaskAsync(hecha);
            await d.Tasks.CompleteTaskAsync(hecha);
            var etiquetada = await d.Repo.AddTaskAsync(lista.Id, "Etiquetada");
            etiquetada.Tags = "casa";
            await d.Repo.UpdateTaskAsync(etiquetada);
        });

        Assert.Equal(["Etiquetada", "Por hacer"], Titulos(ventana.TodoBox).Order().ToList());
        Assert.Equal(["En curso"], Titulos(ventana.DoingBox));
        Assert.Equal(["Hecha"], Titulos(ventana.DoneBox));
        Assert.Equal("2", ventana.TodoCount.Text);
        Assert.Equal(Visibility.Collapsed, ventana.DoneEmpty.Visibility);

        // Filtro de pendientes: la columna de hechas se queda vacia.
        var chips = ventana.KanbanFilterBox.Children.OfType<ToggleButton>().ToList();
        var pendientes = chips.Single(c => (TaskFilter)c.Tag == TaskFilter.Pending);
        await Ui.Marcar(pendientes, true);
        Assert.Empty(ventana.DoneBox.Items);
        Assert.Equal(Visibility.Visible, ventana.DoneEmpty.Visibility);
        await Ui.Marcar(pendientes, false);
        Assert.True(pendientes.IsChecked);

        // Etiqueta.
        Assert.Equal(Visibility.Visible, ventana.KanbanTagScroll.Visibility);
        var casa = ventana.KanbanTagBox.Children.OfType<ToggleButton>().Single(c => (string)c.Content == "#casa");
        await Ui.Pulsar(casa);
        Assert.Equal(["Etiquetada"], Titulos(ventana.TodoBox));
        Assert.Empty(ventana.DoingBox.Items);
        await Ui.Pulsar(ventana.KanbanTagBox.Children.OfType<ToggleButton>().First());

        // Busqueda.
        await Ui.Escribir(ventana.KanbanSearchBox, "curso");
        Assert.Equal(["En curso"], Titulos(ventana.DoingBox));
        Assert.Empty(ventana.TodoBox.Items);
        await Ui.Escribir(ventana.KanbanSearchBox, string.Empty);

        // Crear: con Enter va a «por hacer»; con el otro boton nace en curso; sin texto, nada.
        Ui.Responder(Dialogo.Cerrar, Dialogo.Cerrar, Dialogo.Cerrar);
        ventana.KanbanAddBox.Text = "Nueva";
        await Ui.Tecla(ventana.KanbanAddBox, Key.Enter);
        ventana.KanbanAddBox.Text = "Empezada";
        await Ui.Llamar(ventana, "OnKanbanAddInProgressClick", null, new RoutedEventArgs());
        ventana.KanbanAddBox.Text = "Otra";
        await Ui.Llamar(ventana, "OnKanbanAddClick", null, new RoutedEventArgs());
        await Ui.Tecla(ventana.KanbanAddBox, Key.Enter);
        await Ui.Tecla(ventana.KanbanAddBox, Key.C);
        Assert.Contains("Nueva", Titulos(ventana.TodoBox));
        Assert.Contains("Empezada", Titulos(ventana.DoingBox));
        Assert.Equal(0, Ui.RespuestasPendientes);

        await Ui.Llamar(ventana, "OnKanbanRefreshClick", null, new RoutedEventArgs());
        Assert.Equal(ventana.TodoBox.Items.Count.ToString(), ventana.TodoCount.Text);

        // Sin ninguna etiqueta con algo pendiente, la fila de etiquetas desaparece.
        foreach (var t in await datos.Repo.GetAllTasksAsync(TaskFilter.All))
        {
            await datos.Tasks.CompleteTaskAsync(t);
        }

        await Ui.Llamar(ventana, "OnKanbanRefreshClick", null, new RoutedEventArgs());
        Assert.Equal(Visibility.Collapsed, ventana.KanbanTagScroll.Visibility);
    });

    [Fact]
    public Task Arrastrar_en_el_tablero_cambia_el_estado_o_recoloca() => Ui.Run(async () =>
    {
        var (datos, ventana) = await AbrirAsync(async d =>
        {
            var lista = await d.Repo.CreateListAsync("Casa");
            await d.Repo.AddTaskAsync(lista.Id, "A");
            await d.Repo.AddTaskAsync(lista.Id, "B");
        });

        Guid Id(string titulo) => P<Guid>(ventana.TodoBox.Items.Cast<object>()
            .Concat(ventana.DoingBox.Items.Cast<object>()).Concat(ventana.DoneBox.Items.Cast<object>())
            .First(r => Titulo(r) == titulo), "Id");

        object Fila(string titulo) => ventana.TodoBox.Items.Cast<object>()
            .Concat(ventana.DoingBox.Items.Cast<object>()).Concat(ventana.DoneBox.Items.Cast<object>())
            .First(r => Titulo(r) == titulo);

        // Arrancar el arrastre desde una tarjeta.
        var tarjeta = await Ui.Fila(ventana.TodoBox, Fila("A"));
        Ui.Sistema.Posicion = new Point(0, 0);
        Ui.Invocar(ventana, "OnKanbanDragStart", ventana.TodoBox, Ui.Raton(Ui.DentroDe(tarjeta), UIElement.PreviewMouseLeftButtonDownEvent));
        var mover = new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent };
        Ui.Sistema.Boton = MouseButtonState.Pressed;
        Ui.Invocar(ventana, "OnKanbanDragMove", ventana.TodoBox, mover);
        Assert.Empty(Ui.Sistema.Arrastres);
        Ui.Sistema.Posicion = new Point(200, 0);
        Ui.Invocar(ventana, "OnKanbanDragMove", ventana.TodoBox, mover);
        Assert.Single(Ui.Sistema.Arrastres);
        var encima = Ui.Soltar(Fila("A"), ventana.DoingBox);
        Ui.Invocar(ventana, "OnKanbanDragOver", ventana.DoingBox, encima);
        Assert.Equal(DragDropEffects.Move, encima.Effects);

        // A «en curso».
        await Ui.Llamar(ventana, "OnKanbanDrop", ventana.DoingBox, Ui.Soltar(Fila("A"), ventana.DoingBox));
        Assert.True((await datos.Repo.GetTaskAsync(Id("A")))!.InProgress);
        Assert.Equal(["A"], Titulos(ventana.DoingBox));

        // A «hecha».
        await Ui.Llamar(ventana, "OnKanbanDrop", ventana.DoneBox, Ui.Soltar(Fila("A"), ventana.DoneBox));
        Assert.True((await datos.Repo.GetTaskAsync(Id("A")))!.IsDone);

        // De vuelta a «por hacer»: ni hecha ni en curso.
        await Ui.Llamar(ventana, "OnKanbanDrop", ventana.TodoBox, Ui.Soltar(Fila("A"), ventana.TodoBox));
        var a = (await datos.Repo.GetTaskAsync(Id("A")))!;
        Assert.False(a.IsDone);
        Assert.False(a.InProgress);

        // Dentro de su columna se recoloca: B encima de A.
        var orden = Titulos(ventana.TodoBox);
        var sobre = await Ui.Fila(ventana.TodoBox, Fila(orden[0]));
        await Ui.Llamar(ventana, "OnKanbanDrop", ventana.TodoBox, Ui.Soltar(Fila(orden[1]), Ui.DentroDe(sobre)));
        Assert.Equal([orden[1], orden[0]], Titulos(ventana.TodoBox));

        // Sobre si misma no cambia nada; algo que no es una tarjeta, tampoco; una tarea borrada, tampoco.
        await Ui.Llamar(ventana, "OnKanbanDrop", ventana.TodoBox, Ui.Soltar(Fila(orden[0]), ventana.TodoBox));
        await Ui.Llamar(ventana, "OnKanbanDrop", ventana.TodoBox, Ui.Soltar("texto", ventana.TodoBox));
        var b = Fila("B");
        await datos.Repo.DeleteTaskAsync((await datos.Repo.GetTaskAsync(P<Guid>(b, "Id")))!);
        await Ui.Llamar(ventana, "OnKanbanDrop", ventana.DoneBox, Ui.Soltar(b, ventana.DoneBox));
        Assert.Empty(ventana.DoneBox.Items);
    });

    // ---------------------------------------------------------------------------------
    // Grupos
    // ---------------------------------------------------------------------------------

    [Fact]
    public Task Crear_un_grupo_en_local_y_compartir_su_invitacion() => Ui.Run(async () =>
    {
        var (datos, ventana) = await AbrirAsync();
        Assert.Equal(Visibility.Visible, ventana.GroupsEmpty.Visibility);

        // Sin nombre no se crea.
        Ui.Responder(Dialogo.Cerrar);
        await Ui.Llamar(ventana, "OnNewGroupClick", null, new RoutedEventArgs());
        Assert.Empty(await datos.Repo.GetGroupsAsync());

        // El QR sale con sus tres extras: copiar, correo y WhatsApp.
        Ui.Responder(Dialogo.Escribir("Familia"), w =>
        {
            var botones = Ui.Botones(w);
            Assert.Equal(4, botones.Count);
            Ui.Sistema.Texto = null;
            foreach (var extra in botones.Take(3))
            {
                extra.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }

            Dialogo.Aceptar(w);
        });
        await Ui.Llamar(ventana, "OnNewGroupClick", null, new RoutedEventArgs());

        var grupo = Assert.Single(await datos.Repo.GetGroupsAsync());
        Assert.Equal("Familia", grupo.Name);
        Assert.Single(await datos.Repo.GetGroupListsAsync(grupo.Id));
        Assert.Contains("Familia", Ui.Sistema.Texto);
        Assert.Equal(2, Ui.Sistema.Abiertos.Count);
        Assert.StartsWith("mailto:", Ui.Sistema.Abiertos[0]);
        Assert.StartsWith("https://wa.me/", Ui.Sistema.Abiertos[1]);
        Assert.Single(ventana.GroupsBox.Items);
        Assert.Equal(Visibility.Collapsed, ventana.GroupsEmpty.Visibility);

        // Las listas del grupo salen en «Mis listas» con el nombre delante.
        Assert.Contains(ventana.ListsBox.Items.Cast<object>(), l => P<string>(l, "Caption").StartsWith("Familia"));

        // Sin navegador, se dice.
        Ui.Sistema.FalloAlAbrir = new InvalidOperationException("sin navegador");
        Ui.Responder(Dialogo.Aceptar);
        Ui.Invocar(ventana, "Abrir", "mailto:x");
        Assert.Contains("sin navegador", Dialogo.Leidos.Last());

        // Invitar de nuevo sin servidor no se puede.
        Ui.Responder(Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnInviteClick", ConTag(grupo.Id), new RoutedEventArgs());
        Assert.Contains(Localization.Loc.Get("GroupCodeLocal"), Dialogo.Leidos.Last());
        await Ui.Llamar(ventana, "OnInviteClick", ConTag(Guid.NewGuid()), new RoutedEventArgs());
        await Ui.Llamar(ventana, "OnInviteClick", new Button(), new RoutedEventArgs());

        // Nueva lista en el grupo.
        Ui.Responder(Dialogo.Cerrar, Dialogo.Escribir("Compra"));
        await Ui.Llamar(ventana, "OnNewGroupListClick", ConTag(grupo.Id), new RoutedEventArgs());
        await Ui.Llamar(ventana, "OnNewGroupListClick", ConTag(grupo.Id), new RoutedEventArgs());
        await Ui.Llamar(ventana, "OnNewGroupListClick", new Button(), new RoutedEventArgs());
        Assert.Equal(2, (await datos.Repo.GetGroupListsAsync(grupo.Id)).Count);

        // Entrar en otro sin servidor: se dice que no se puede.
        Ui.Responder(Dialogo.Escribir("XYZ"), Dialogo.Escribir("clave"), Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnJoinGroupClick", null, new RoutedEventArgs());
        Assert.Contains(Localization.Loc.Get("GroupCodeLocal"), Dialogo.Leidos.Last());

        // Salir del grupo en local: se pregunta como y se confirma.
        Ui.Responder(Dialogo.Cerrar);
        await Ui.Llamar(ventana, "OnLeaveGroupClick", ConTag(grupo.Id), new RoutedEventArgs());
        Ui.Responder(Dialogo.Elegir(0), Dialogo.Primero);
        await Ui.Llamar(ventana, "OnLeaveGroupClick", ConTag(grupo.Id), new RoutedEventArgs());
        Assert.Single(await datos.Repo.GetGroupsAsync());
        Ui.Responder(Dialogo.Elegir(1), Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnLeaveGroupClick", ConTag(grupo.Id), new RoutedEventArgs());
        Assert.Empty(await datos.Repo.GetGroupsAsync());
        await Ui.Llamar(ventana, "OnLeaveGroupClick", ConTag(Guid.NewGuid()), new RoutedEventArgs());
        await Ui.Llamar(ventana, "OnLeaveGroupClick", new Button(), new RoutedEventArgs());
    });

    [Fact]
    public Task Grupos_con_servidor_crear_invitar_entrar_y_borrar() => Ui.Run(async () =>
    {
        var sync = new SyncFalso();
        var (datos, ventana) = await AbrirAsync(sync: sync);

        Ui.Responder(Dialogo.Escribir("Equipo"), Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnNewGroupClick", null, new RoutedEventArgs());
        var grupo = Assert.Single(await datos.Repo.GetGroupsAsync());
        Assert.Equal("ABC123", grupo.JoinCode);
        Assert.Contains("clave-nueva", Ui.Sistema.Texto);

        // Si el servidor lo rechaza, el grupo no se queda a medias.
        sync.Fallo = new InvalidOperationException("servidor caido");
        Ui.Responder(Dialogo.Escribir("Roto"), Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnNewGroupClick", null, new RoutedEventArgs());
        Assert.Single(await datos.Repo.GetGroupsAsync());
        Assert.Contains("servidor caido", Dialogo.Leidos.Last());

        // Invitar: falla y se dice; despues va.
        Ui.Responder(Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnInviteClick", ConTag(grupo.Id), new RoutedEventArgs());
        Assert.Contains("servidor caido", Dialogo.Leidos.Last());
        sync.Fallo = null;
        Ui.Responder(Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnInviteClick", ConTag(grupo.Id), new RoutedEventArgs());
        Assert.Contains("clave-renovada", Ui.Sistema.Texto);

        // Entrar con codigo y clave; sin codigo o sin clave no se intenta.
        Ui.Responder(Dialogo.Cerrar);
        await Ui.Llamar(ventana, "OnJoinGroupClick", null, new RoutedEventArgs());
        Ui.Responder(Dialogo.Escribir("COD"), Dialogo.Cerrar);
        await Ui.Llamar(ventana, "OnJoinGroupClick", null, new RoutedEventArgs());
        Assert.DoesNotContain(sync.Llamadas, l => l.StartsWith("entrar"));
        Ui.Responder(Dialogo.Escribir(" COD "), Dialogo.Escribir(" k "), Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnJoinGroupClick", null, new RoutedEventArgs());
        Assert.Contains("entrar:COD:k", sync.Llamadas);
        Assert.Contains(Localization.Loc.Get("GroupCodeShare"), Dialogo.Leidos.Last());

        // Borrar para todos sin ser el dueño: no se deja.
        sync.Propietario = false;
        Ui.Responder(Dialogo.Elegir(1), Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnLeaveGroupClick", ConTag(grupo.Id), new RoutedEventArgs());
        Assert.Contains(Localization.Loc.Get("GroupNotOwner"), Dialogo.Leidos.Last());

        // Siendo el dueño, pero cancelando la confirmacion.
        sync.Propietario = true;
        Ui.Responder(Dialogo.Elegir(1), Dialogo.Primero);
        await Ui.Llamar(ventana, "OnLeaveGroupClick", ConTag(grupo.Id), new RoutedEventArgs());
        Assert.DoesNotContain("borrar", sync.Llamadas);

        // Sin red no se sale: quitarlo de aqui lo traeria de vuelta.
        sync.Fallo = new HttpRequestException("sin red");
        Ui.Responder(Dialogo.Elegir(0), Dialogo.Aceptar, Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnLeaveGroupClick", ConTag(grupo.Id), new RoutedEventArgs());
        Assert.Single(await datos.Repo.GetGroupsAsync());
        Assert.Contains(Dialogo.Leidos.Last(), t => t.Contains("sin red"));

        // Salir.
        sync.Fallo = null;
        Ui.Responder(Dialogo.Elegir(0), Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnLeaveGroupClick", ConTag(grupo.Id), new RoutedEventArgs());
        Assert.Contains("salir", sync.Llamadas);
        Assert.Empty(await datos.Repo.GetGroupsAsync());

        // Y borrar para todos otro.
        var otro = await datos.Repo.SaveGroupAsync(new TaskGroup { Name = "Otro", JoinCode = "Q" });
        Ui.Responder(Dialogo.Elegir(1), Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnLeaveGroupClick", ConTag(otro.Id), new RoutedEventArgs());
        Assert.Contains("borrar", sync.Llamadas);
        Assert.Empty(await datos.Repo.GetGroupsAsync());
    });

    [Fact]
    public Task Entrar_desde_un_enlace_o_un_QR() => Ui.Run(async () =>
    {
        var sync = new SyncFalso();
        var (_, ventana) = await AbrirAsync(sync: sync);
        var invite = new GroupInvite("QR1234", "clave-qr");
        var png = GroupLink.QrPng(GroupLink.For(invite));
        var fichero = Path.Combine(Ui.Carpeta, "qr.png");
        await File.WriteAllBytesAsync(fichero, png);

        // Enlace dejado por otra copia: se pregunta; diciendo que no, no se entra, pero se recoge.
        GroupLinkProtocol.Dejar(invite);
        Ui.Responder(Dialogo.Primero);
        await Ui.Hacer(ventana.AtenderInvitacion);
        Assert.True(ventana.GroupsTab.IsSelected);
        Assert.False(File.Exists(GroupLinkProtocol.BuzonPath));
        Assert.DoesNotContain(sync.Llamadas, l => l.StartsWith("entrar"));

        // Sin nada en el buzon no hace nada.
        await Ui.Hacer(ventana.AtenderInvitacion);

        // Con el si, se entra.
        GroupLinkProtocol.Dejar(invite);
        Ui.Responder(Dialogo.Aceptar, Dialogo.Aceptar);
        await Ui.Hacer(ventana.AtenderInvitacion);
        Assert.Contains("entrar:QR1234:clave-qr", sync.Llamadas);

        // QR de un fichero; cancelar el cuadro no hace nada.
        sync.Llamadas.Clear();
        Ui.Responder(Dialogo.Elegir(0));
        await Ui.Llamar(ventana, "OnScanQrClick", null, new RoutedEventArgs());
        Ui.Sistema.FicherosElegidos.Enqueue(fichero);
        Ui.Responder(Dialogo.Elegir(0), Dialogo.Aceptar, Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnScanQrClick", null, new RoutedEventArgs());
        Assert.Contains("entrar:QR1234:clave-qr", sync.Llamadas);

        // Del portapapeles, con el enlace copiado como texto.
        sync.Llamadas.Clear();
        Ui.Sistema.Texto = GroupLink.For(invite).ToString();
        Ui.Responder(Dialogo.Elegir(1), Dialogo.Aceptar, Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnScanQrClick", null, new RoutedEventArgs());
        Assert.Contains("entrar:QR1234:clave-qr", sync.Llamadas);

        // De la pantalla: no hay ningun QR y se dice; la ventana vuelve a como estaba.
        Ui.Responder(Dialogo.Elegir(2), Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnScanQrClick", null, new RoutedEventArgs());
        Assert.Contains(Localization.Loc.Get("ScanNotFound"), Dialogo.Leidos.Last());
        Assert.Equal(WindowState.Normal, ventana.WindowState);

        // Cerrar sin elegir de donde.
        Ui.Responder(Dialogo.Cerrar);
        await Ui.Llamar(ventana, "OnScanQrClick", null, new RoutedEventArgs());

        // Con el servidor fallando, se dice en vez de fingir que ha entrado.
        sync.Fallo = new InvalidOperationException("clave mala");
        Ui.Responder(Dialogo.Elegir(1), Dialogo.Aceptar, Dialogo.Aceptar);
        await Ui.Llamar(ventana, "OnScanQrClick", null, new RoutedEventArgs());
        Assert.Contains("clave mala", Dialogo.Leidos.Last());
    });

    [Fact]
    public Task Las_filas_de_tarea_pintan_fechas_pasos_etiquetas_y_lista() => Ui.Run(async () =>
    {
        var (_, ventana) = await AbrirAsync(async d =>
        {
            var lista = await d.Repo.CreateListAsync("Casa");
            var t = await d.Repo.AddTaskAsync(lista.Id, "Completa");
            t.PlannedFor = new DateTime(2026, 3, 2);
            t.DueAt = new DateTime(2026, 3, 9);
            t.Tags = "a,b";
            t.IsPinned = true;
            await d.Repo.UpdateTaskAsync(t);
            await d.Repo.AddStepsAsync(t.Id, ["uno", "dos"]);
            var hecha = await d.Repo.AddTaskAsync(lista.Id, "Hecha");
            await d.Tasks.CompleteTaskAsync(hecha);
        });

        var todas = ventana.FilterBox.Children.OfType<ToggleButton>().Single(c => (TaskFilter)c.Tag == TaskFilter.All);
        await Ui.Marcar(todas, true);

        var completa = ventana.AllTasksBox.Items.Cast<object>().Single(r => Titulo(r).Contains("Completa"));
        Assert.StartsWith("📌 ", Titulo(completa));
        var pie = P<string>(completa, "Caption");
        Assert.Contains("0/2", pie);
        Assert.Contains("#a  #b", pie);
        Assert.EndsWith("Casa", pie);
        Assert.Equal(Visibility.Visible, P<Visibility>(completa, "CaptionVisibility"));
        Assert.Null(completa.GetType().GetProperty("Decoration")!.GetValue(completa));
        Assert.Equal(1.0, P<double>(completa, "Opacity"));

        var hecha = ventana.AllTasksBox.Items.Cast<object>().Single(r => Titulo(r) == "Hecha");
        Assert.Same(TextDecorations.Strikethrough, P<TextDecorationCollection>(hecha, "Decoration"));
        Assert.Equal(0.55, P<double>(hecha, "Opacity"));

        var deLista = ventana.ListTasksBox.Items.Cast<object>().Single(r => Titulo(r) == "Hecha");
        Assert.Equal(Visibility.Collapsed, P<Visibility>(deLista, "CaptionVisibility"));
    });
}
