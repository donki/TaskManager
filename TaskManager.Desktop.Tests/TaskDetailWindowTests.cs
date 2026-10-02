using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using TaskManager.Core.Models;
using TaskManager.Desktop.Services;
using TaskManager.Desktop.Tests.Banco;

namespace TaskManager.Desktop.Tests;

/// <summary>La ficha de una tarea: campos, fechas, repeticion, etiquetas, pasos y adjuntos.</summary>
public class TaskDetailWindowTests
{
    /// <summary>
    /// Abre la ficha como la abre la aplicacion —modal— y hace en ella lo que diga el guion. Si el
    /// guion no la cierra, se cierra al acabar.
    /// </summary>
    private static async Task<TaskDetailWindow> Ficha(Datos datos, TaskItem tarea, Func<TaskDetailWindow, Task> guion)
    {
        var ventana = new TaskDetailWindow(datos.Tasks, tarea);
        Ui.ResponderAsync(async w =>
        {
            await Ui.Hasta(() => ventana.ListBox.Items.Count > 0, que: "las listas de la ficha");
            await Ui.Calma();
            await guion(ventana);
            if (ventana.IsVisible)
            {
                ventana.Close();
            }
        });

        await Ui.Hacer(() => Ventanas.Modal(ventana), 60000);
        return ventana;
    }

    private static async Task<(Datos Datos, TaskList Lista, TaskItem Tarea)> TareaAsync(string titulo = "Tarea")
    {
        var datos = await Ui.Datos();
        var lista = await datos.Repo.CreateListAsync("Casa");
        var tarea = await datos.Repo.AddTaskAsync(lista.Id, titulo);
        return (datos, lista, tarea);
    }

    private static Task Click(object ventana, string metodo, object? sender = null) =>
        Ui.Llamar(ventana, metodo, sender, new RoutedEventArgs());

    private static string Leer(object fila, string p) => MainWindowTests.P<string>(fila, p);

    [Fact]
    public Task Guardar_los_campos_y_cambiar_de_lista() => Ui.Run(async () =>
    {
        var (datos, _, tarea) = await TareaAsync();
        var otra = await datos.Repo.CreateListAsync("Trabajo");

        var ficha = await Ficha(datos, tarea, async f =>
        {
            Assert.Equal("Tarea", f.TitleBox.Text);
            Assert.Equal(Visibility.Collapsed, f.DueRow.Visibility);
            Assert.Equal(Visibility.Collapsed, f.IntervalPanel.Visibility);

            f.TitleBox.Text = "  Nueva  ";
            f.NotesBox.Text = " notas ";
            f.ListBox.SelectedIndex = f.ListBox.Items.Cast<string>().ToList().IndexOf("Trabajo");

            // Fechas: se abren las filas y se ponen dia y hora.
            f.DueCheck.IsChecked = true;
            await Click(f, "OnDueToggled");
            Assert.Equal(Visibility.Visible, f.DueRow.Visibility);
            f.DuePicker.SelectedDate = new DateTime(2026, 5, 20);
            f.DueTime.SelectedTime = new DateTime(1, 1, 1, 17, 30, 0);
            f.PlannedCheck.IsChecked = true;
            await Click(f, "OnPlannedToggled");
            f.PlannedPicker.SelectedDate = new DateTime(2026, 5, 18);
            f.PlannedTime.SelectedTime = null;

            await Ui.Pulsar(f.SaveButton);
        });

        Assert.True(ficha.Changed);
        Assert.False(ficha.Deleted);
        var guardada = (await datos.Repo.GetTaskAsync(tarea.Id))!;
        Assert.Equal("Nueva", guardada.Title);
        Assert.Equal("notas", guardada.Notes);
        Assert.Equal(otra.Id, guardada.ListId);
        Assert.Equal(new DateTime(2026, 5, 20, 17, 30, 0), guardada.DueAt);
        Assert.Equal(new DateTime(2026, 5, 18, 9, 0, 0), guardada.PlannedFor);
    });

    [Fact]
    public Task Guardar_sin_titulo_o_repetir_sin_fechas_se_avisa_y_no_guarda() => Ui.Run(async () =>
    {
        var (datos, _, tarea) = await TareaAsync();

        var ficha = await Ficha(datos, tarea, async f =>
        {
            f.TitleBox.Text = "   ";
            await Ui.Pulsar(f.SaveButton);
            Assert.Equal(Localization.Loc.Get("TitleRequired"), f.StatusLabel.Text);

            f.TitleBox.Text = "Con titulo";
            f.RecurrenceBox.SelectedIndex = 1;
            await Ui.Pulsar(f.SaveButton);
            Assert.Equal(Localization.Loc.Get("RecurrenceNeedsDates"), f.StatusLabel.Text);
            Assert.Equal(Visibility.Visible, f.DueRow.Visibility);
            Assert.Equal(Visibility.Visible, f.PlannedRow.Visibility);
            Assert.True(f.DueCheck.IsChecked);
        });

        Assert.False(ficha.Changed);
        Assert.Equal([TipoAviso.Advertencia, TipoAviso.Advertencia], Ui.Sistema.Avisos.Select(a => a.Tipo));
        Assert.Equal("Tarea", (await datos.Repo.GetTaskAsync(tarea.Id))!.Title);
    });

    [Fact]
    public Task Repeticion_semanal_con_dias_genera_la_serie() => Ui.Run(async () =>
    {
        var (datos, _, tarea) = await TareaAsync("Regar");
        tarea.PlannedFor = DateTime.Today;
        tarea.DueAt = DateTime.Today.AddDays(30);
        await datos.Repo.UpdateTaskAsync(tarea);

        await Ficha(datos, tarea, async f =>
        {
            // Semanal: salen los dias, no el dia del mes.
            f.RecurrenceBox.SelectedIndex = 2;
            Assert.Equal(Visibility.Visible, f.IntervalPanel.Visibility);
            Assert.Equal(Visibility.Visible, f.WeekdaysBox.Visibility);
            Assert.Equal(Visibility.Collapsed, f.MonthDayRow.Visibility);

            // Cada 2 semanas: subir dos y bajar una; nunca por debajo de 1.
            await Click(f, "OnIntervalUp");
            await Click(f, "OnIntervalUp");
            await Click(f, "OnIntervalDown");
            Assert.Equal("2", f.IntervalLabel.Text);
            for (var i = 0; i < 3; i++)
            {
                await Click(f, "OnIntervalDown");
            }

            Assert.Equal("1", f.IntervalLabel.Text);

            // Lunes y miercoles; marcar y desmarcar el viernes no deja nada.
            var dias = f.WeekdaysBox.Children.OfType<ToggleButton>().ToList();
            Assert.Equal(7, dias.Count);
            foreach (var i in new[] { 0, 2, 4 })
            {
                dias[i].IsChecked = true;
                await Ui.Pulsar(dias[i]);
            }

            dias[4].IsChecked = false;
            await Ui.Pulsar(dias[4]);
            Assert.False(string.IsNullOrEmpty(f.RecurrenceLabel.Text));

            await Ui.Pulsar(f.SaveButton);
        });

        var guardada = (await datos.Repo.GetTaskAsync(tarea.Id))!;
        var regla = guardada.Recurrence;
        Assert.Equal(RecurrenceKind.Weekly, regla.Kind);
        Assert.Equal((1 << (int)DayOfWeek.Monday) | (1 << (int)DayOfWeek.Wednesday), regla.Days);
        Assert.NotNull(guardada.SeriesId);
        Assert.True((await datos.Repo.GetSeriesAsync(guardada.SeriesId!.Value)).Count > 1);
        Assert.Equal(TipoAviso.Exito, Assert.Single(Ui.Sistema.Avisos).Tipo);
    });

    [Fact]
    public Task Repeticion_mensual_y_anual_y_serie_truncada() => Ui.Run(async () =>
    {
        var (datos, _, tarea) = await TareaAsync("Pagar");
        tarea.PlannedFor = DateTime.Today;
        tarea.DueAt = DateTime.Today.AddYears(3);
        tarea.RecurrenceRule = new Recurrence(RecurrenceKind.Yearly, 1, 0, 15, 6).Serialize();
        await datos.Repo.UpdateTaskAsync(tarea);

        await Ficha(datos, tarea, async f =>
        {
            // Se abre con lo que tenia: anual, dia 15 de junio.
            Assert.Equal(4, f.RecurrenceBox.SelectedIndex);
            Assert.Equal(15, f.MonthDayBox.SelectedIndex);
            Assert.Equal(6, f.MonthBox.SelectedIndex);
            Assert.Equal(Visibility.Visible, f.MonthBox.Visibility);

            // Mensual: sin mes; el dia 1.
            f.RecurrenceBox.SelectedIndex = 3;
            Assert.Equal(Visibility.Collapsed, f.MonthBox.Visibility);
            Assert.Equal(Visibility.Visible, f.MonthDayRow.Visibility);
            f.MonthDayBox.SelectedIndex = 1;
            f.MonthBox.SelectedIndex = 0;

            // Y diaria, que en tres años pasa del tope y se corta.
            f.RecurrenceBox.SelectedIndex = 1;
            await Ui.Pulsar(f.SaveButton);
        });

        var guardada = (await datos.Repo.GetTaskAsync(tarea.Id))!;
        Assert.Equal(RecurrenceKind.Daily, guardada.Recurrence.Kind);
        var aviso = Assert.Single(Ui.Sistema.Avisos);
        Assert.Equal(Localization.Loc.Format("SeriesTruncated", Recurrence.MaxOccurrences, Recurrence.MaxOccurrences), aviso.Texto);
    });

    [Fact]
    public Task Casillas_de_hecha_anclada_y_en_curso_guardan_al_momento() => Ui.Run(async () =>
    {
        var (datos, _, tarea) = await TareaAsync();

        var ficha = await Ficha(datos, tarea, async f =>
        {
            f.PinCheck.IsChecked = true;
            await Click(f, "OnPinToggled");
            Assert.True((await datos.Repo.GetTaskAsync(tarea.Id))!.IsPinned);

            f.ProgressCheck.IsChecked = true;
            await Click(f, "OnProgressToggled");
            Assert.True((await datos.Repo.GetTaskAsync(tarea.Id))!.InProgress);

            // Hecha apaga «en curso»; deshacerla la devuelve.
            f.DoneCheck.IsChecked = true;
            await Click(f, "OnDoneToggled");
            Assert.True((await datos.Repo.GetTaskAsync(tarea.Id))!.IsDone);
            Assert.False(f.ProgressCheck.IsChecked);

            f.DoneCheck.IsChecked = false;
            await Click(f, "OnDoneToggled");
            Assert.False((await datos.Repo.GetTaskAsync(tarea.Id))!.IsDone);

            // Empezar algo hecho lo devuelve a pendiente.
            f.DoneCheck.IsChecked = true;
            await Click(f, "OnDoneToggled");
            f.ProgressCheck.IsChecked = true;
            await Click(f, "OnProgressToggled");
            Assert.False(f.DoneCheck.IsChecked);
        });

        Assert.True(ficha.Changed);
    });

    [Fact]
    public Task Etiquetas_se_escriben_se_reutilizan_y_se_quitan() => Ui.Run(async () =>
    {
        var (datos, lista, tarea) = await TareaAsync();
        var otra = await datos.Repo.AddTaskAsync(lista.Id, "Otra");
        otra.Tags = "casa";
        await datos.Repo.UpdateTaskAsync(otra);
        tarea.Tags = "urgente";
        await datos.Repo.UpdateTaskAsync(tarea);

        await Ficha(datos, tarea, async f =>
        {
            await Ui.Hasta(() => f.KnownTagsBox.Children.Count == 2, que: "las etiquetas conocidas");
            Assert.Single(f.TagsContainer.Items);

            // Escribir dos a la vez, con Enter; vacio no hace nada; repetida no se duplica.
            await Ui.Tecla(f.TagsBox, Key.Enter);
            f.TagsBox.Text = "compra, Urgente";
            await Ui.Tecla(f.TagsBox, Key.Enter);
            await Ui.Tecla(f.TagsBox, Key.Tab);
            Assert.Equal(2, f.TagsContainer.Items.Count);
            Assert.Equal(string.Empty, f.TagsBox.Text);
            f.TagsBox.Text = "extra";
            await Click(f, "OnAddTagClick");
            Assert.Equal(3, f.TagsContainer.Items.Count);

            // Pulsar una conocida la pone; pulsarla otra vez la quita.
            var casa = f.KnownTagsBox.Children.OfType<ToggleButton>().Single(c => (string)c.Content == "#casa");
            await Ui.Pulsar(casa);
            Assert.Equal(4, f.TagsContainer.Items.Count);
            casa = f.KnownTagsBox.Children.OfType<ToggleButton>().Single(c => (string)c.Content == "#casa");
            Assert.True(casa.IsChecked);
            await Ui.Pulsar(casa);
            Assert.Equal(3, f.TagsContainer.Items.Count);

            // El aspa de una pastilla la quita.
            var extra = f.TagsContainer.Items.OfType<HandyControl.Controls.Tag>().Single(t => (string)t.Content == "extra");
            var cerrada = (RoutedEvent)typeof(HandyControl.Controls.Tag).GetField("ClosedEvent")!.GetValue(null)!;
            await Ui.Hacer(() => extra.RaiseEvent(new RoutedEventArgs(cerrada, extra)));
            Assert.Equal(2, f.TagsContainer.Items.Count);

            await Ui.Pulsar(f.SaveButton);
        });

        var guardada = (await datos.Repo.GetTaskAsync(tarea.Id))!;
        Assert.Equal(["compra", "urgente"], TaskTags.Split(guardada.Tags).Order());
    });

    [Fact]
    public Task Pasos_se_añaden_marcan_renombran_reordenan_y_borran() => Ui.Run(async () =>
    {
        var (datos, _, tarea) = await TareaAsync();

        var ficha = await Ficha(datos, tarea, async f =>
        {
            Assert.Equal(Visibility.Visible, f.NoStepsLabel.Visibility);

            // Con Enter y con el boton; vacio no añade.
            await Ui.Tecla(f.NewStepBox, Key.Enter);
            f.NewStepBox.Text = "Uno";
            await Ui.Tecla(f.NewStepBox, Key.Enter);
            await Ui.Tecla(f.NewStepBox, Key.Space);
            f.NewStepBox.Text = "Dos";
            await Click(f, "OnAddStepClick");
            Assert.Equal(2, f.StepsBox.Items.Count);
            Assert.Equal(Visibility.Collapsed, f.NoStepsLabel.Visibility);

            var pasos = await datos.Repo.GetStepsAsync(tarea.Id);
            var uno = pasos.Single(p => p.Title == "Uno");

            // Marcar.
            await Click(f, "OnStepToggled", new CheckBox { Tag = uno.Id });
            Assert.True((await datos.Repo.GetStepAsync(uno.Id))!.IsDone);
            var fila = f.StepsBox.Items.Cast<object>().Single(r => Leer(r, "Title") == "Uno");
            Assert.Equal(0.55, MainWindowTests.P<double>(fila, "Opacity"));
            Assert.NotNull(fila.GetType().GetProperty("Decoration")!.GetValue(fila));
            await Click(f, "OnStepToggled", new CheckBox());
            await Click(f, "OnStepToggled", new CheckBox { Tag = Guid.NewGuid() });

            // Renombrar con doble clic; vacio no cambia.
            var item = await Ui.Fila(f.StepsBox, fila);
            Ui.Responder(Dialogo.Escribir(" "), Dialogo.Escribir("Uno bis"));
            await Ui.Llamar(f, "OnStepDoubleClick", f.StepsBox, Ui.Raton(Ui.DentroDe(item)));
            await Ui.Llamar(f, "OnStepDoubleClick", f.StepsBox, Ui.Raton(Ui.DentroDe(item)));
            Assert.Equal("Uno bis", (await datos.Repo.GetStepAsync(uno.Id))!.Title);
            await Ui.Llamar(f, "OnStepDoubleClick", f.StepsBox, Ui.Raton(f.StepsBox));

            // Arrastrar «Dos» encima del primero.
            var filas = f.StepsBox.Items.Cast<object>().ToList();
            var primera = await Ui.Fila(f.StepsBox, filas[0]);
            var segunda = await Ui.Fila(f.StepsBox, filas[1]);
            Ui.Sistema.Posicion = new Point(5, 5);
            Ui.Invocar(f, "OnStepMouseDown", f.StepsBox, Ui.Raton(Ui.DentroDe(segunda), UIElement.PreviewMouseLeftButtonDownEvent));
            Ui.Sistema.Boton = MouseButtonState.Pressed;
            Ui.Sistema.Posicion = new Point(5, 60);
            Ui.Invocar(f, "OnStepMouseMove", f.StepsBox, new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent, Source = Ui.DentroDe(segunda) });
            Assert.Same(filas[1], Assert.Single(Ui.Sistema.Arrastres).Datos);
            Ui.Sistema.Boton = MouseButtonState.Released;
            Ui.Invocar(f, "OnStepMouseMove", f.StepsBox, new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
            Assert.Single(Ui.Sistema.Arrastres);

            await Ui.Llamar(f, "OnStepDrop", f.StepsBox, Ui.Soltar(filas[1], Ui.DentroDe(primera)));
            Assert.Equal(Leer(filas[1], "Title"), (await datos.Repo.GetStepsAsync(tarea.Id))[0].Title);
            await Ui.Llamar(f, "OnStepDrop", f.StepsBox, Ui.Soltar(filas[1], Ui.DentroDe(primera)));
            await Ui.Llamar(f, "OnStepDrop", f.StepsBox, Ui.Soltar("texto", f.StepsBox));

            // Borrar.
            await Click(f, "OnDeleteStepClick", new Button { Tag = uno.Id });
            await Click(f, "OnDeleteStepClick", new Button { Tag = Guid.NewGuid() });
            await Click(f, "OnDeleteStepClick", new Button());
            Assert.Single(await datos.Repo.GetStepsAsync(tarea.Id));
        });

        Assert.True(ficha.Changed);
    });

    [Fact]
    public Task Adjuntos_enlaces_y_ficheros() => Ui.Run(async () =>
    {
        var (datos, _, tarea) = await TareaAsync();
        var fichero = Path.Combine(Ui.Carpeta, "nota.txt");
        await File.WriteAllTextAsync(fichero, "hola");
        var grande = Path.Combine(Ui.Carpeta, "grande.bin");
        using (var fs = File.Create(grande))
        {
            fs.SetLength(TaskAttachment.MaxFileBytes + 1);
        }

        await Ficha(datos, tarea, async f =>
        {
            Assert.Equal(Visibility.Visible, f.NoAttachmentsLabel.Visibility);

            // Enlace: cancelar no añade; despues uno sin esquema.
            Ui.Responder(Dialogo.Cerrar, Dialogo.Escribir("ipssoft.com"));
            await Click(f, "OnAddLinkClick");
            await Click(f, "OnAddLinkClick");
            var enlace = Assert.Single(f.AttachmentsBox.Items.Cast<object>());
            Assert.Equal(new Uri("https://ipssoft.com"), MainWindowTests.P<Uri>(enlace, "Link"));
            Assert.Equal("", Leer(enlace, "Glyph"));
            Assert.Equal(Visibility.Visible, MainWindowTests.P<Visibility>(enlace, "LinkVisibility"));
            Assert.Equal(Visibility.Collapsed, MainWindowTests.P<Visibility>(enlace, "FileVisibility"));
            Assert.Equal(Visibility.Collapsed, MainWindowTests.P<Visibility>(enlace, "CaptionVisibility"));

            // Fichero: cancelar el cuadro, uno grande (se avisa), uno que no existe y uno bueno.
            await Click(f, "OnAddFileClick");
            Ui.Sistema.FicherosElegidos.Enqueue(grande);
            Ui.Sistema.FicherosElegidos.Enqueue(Path.Combine(Ui.Carpeta, "no-existe.txt"));
            Ui.Sistema.FicherosElegidos.Enqueue(fichero);
            Ui.Responder(Dialogo.Aceptar, Dialogo.Aceptar);
            await Click(f, "OnAddFileClick");
            Assert.Contains(Dialogo.Leidos.Last(), t => t == Localization.Loc.Format("FileTooBig", 5));
            await Click(f, "OnAddFileClick");
            await Click(f, "OnAddFileClick");
            Assert.Equal(2, f.AttachmentsBox.Items.Count);
            var adjunto = f.AttachmentsBox.Items.Cast<object>().Single(a => Leer(a, "Name") == "nota.txt");
            Assert.Null(adjunto.GetType().GetProperty("Link")!.GetValue(adjunto));
            Assert.Equal(Visibility.Visible, MainWindowTests.P<Visibility>(adjunto, "CaptionVisibility"));

            // Un clic en el enlace lo abre; sin navegador, se dice.
            await Ui.Hacer(() => Ui.Invocar(f, "OnLinkClick", null,
                new System.Windows.Navigation.RequestNavigateEventArgs(new Uri("https://ipssoft.com/"), null)));
            Assert.Equal("https://ipssoft.com/", Ui.Sistema.Abiertos.Last());

            // Doble clic: el fichero se vuelca al temporal y se abre; el enlace va al navegador.
            var filaFichero = await Ui.Fila(f.AttachmentsBox, adjunto);
            await Ui.Llamar(f, "OnAttachmentDoubleClick", f.AttachmentsBox, Ui.Raton(Ui.DentroDe(filaFichero)));
            Assert.EndsWith("nota.txt", Ui.Sistema.Abiertos.Last());
            Assert.Equal("hola", await File.ReadAllTextAsync(Ui.Sistema.Abiertos.Last()));
            var filaEnlace = await Ui.Fila(f.AttachmentsBox, f.AttachmentsBox.Items.Cast<object>().Single(a => Leer(a, "Glyph") == ""));
            await Ui.Llamar(f, "OnAttachmentDoubleClick", f.AttachmentsBox, Ui.Raton(Ui.DentroDe(filaEnlace)));
            Assert.Equal("https://ipssoft.com", Ui.Sistema.Abiertos.Last());
            await Ui.Llamar(f, "OnAttachmentDoubleClick", f.AttachmentsBox, Ui.Raton(f.AttachmentsBox));

            Ui.Sistema.FalloAlAbrir = new InvalidOperationException("sin programa");
            Ui.Responder(Dialogo.Aceptar, Dialogo.Aceptar);
            await Ui.Llamar(f, "OnAttachmentDoubleClick", f.AttachmentsBox, Ui.Raton(Ui.DentroDe(filaFichero)));
            await Ui.Hacer(() => Ui.Invocar(f, "OnLinkClick", null,
                new System.Windows.Navigation.RequestNavigateEventArgs(new Uri("https://x.y/"), null)));
            Assert.Contains("sin programa", Dialogo.Leidos.Last());

            // Borrar.
            var id = MainWindowTests.P<Guid>(adjunto, "Id");
            await Click(f, "OnDeleteAttachmentClick", new Button { Tag = id });
            await Click(f, "OnDeleteAttachmentClick", new Button { Tag = id });
            await Click(f, "OnDeleteAttachmentClick", new Button());
            Assert.Single(f.AttachmentsBox.Items);

            // Un adjunto borrado desde otro sitio ya no se abre.
            Ui.Sistema.FalloAlAbrir = null;
            var queda = f.AttachmentsBox.Items.Cast<object>().Single();
            await datos.Repo.DeleteAttachmentAsync((await datos.Repo.GetAttachmentAsync(MainWindowTests.P<Guid>(queda, "Id")))!);
            await Ui.Llamar(f, "OnAttachmentDoubleClick", f.AttachmentsBox, Ui.Raton(Ui.DentroDe(await Ui.Fila(f.AttachmentsBox, queda))));
        });
    });

    [Fact]
    public Task Pegar_del_portapapeles_imagenes_y_ficheros() => Ui.Run(async () =>
    {
        var (datos, _, tarea) = await TareaAsync();
        var fichero = Path.Combine(Ui.Carpeta, "copiado.txt");
        await File.WriteAllTextAsync(fichero, "x");
        var grande = Path.Combine(Ui.Carpeta, "enorme.bin");
        using (var fs = File.Create(grande))
        {
            fs.SetLength(TaskAttachment.MaxFileBytes + 1);
        }

        await Ficha(datos, tarea, async f =>
        {
            // Nada que pegar.
            Ui.Responder(Dialogo.Aceptar);
            await Click(f, "OnPasteAttachmentClick");
            Assert.Contains(Localization.Loc.Get("PasteNothing"), Dialogo.Leidos.Last());

            // Una imagen, con Ctrl+V.
            Ui.Sistema.Imagen = BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[16], 8);
            Ui.Sistema.Teclas = ModifierKeys.Control;
            await Ui.Tecla(f, Key.V, Keyboard.PreviewKeyDownEvent);
            var imagen = Assert.Single(await datos.Repo.GetAttachmentsAsync(tarea.Id));
            Assert.EndsWith(".png", imagen.Name);

            // V sin Ctrl, u otra tecla con Ctrl, no pegan.
            Ui.Sistema.Teclas = ModifierKeys.None;
            await Ui.Tecla(f, Key.V, Keyboard.PreviewKeyDownEvent);
            Ui.Sistema.Teclas = ModifierKeys.Control;
            await Ui.Tecla(f, Key.C, Keyboard.PreviewKeyDownEvent);
            Ui.Sistema.Imagen = null;
            await Ui.Tecla(f, Key.V, Keyboard.PreviewKeyDownEvent);
            Assert.Single(await datos.Repo.GetAttachmentsAsync(tarea.Id));

            // Ficheros copiados en el Explorador: el que no existe se salta y el grande se avisa.
            Ui.Sistema.Ficheros = [fichero, Path.Combine(Ui.Carpeta, "fantasma.txt"), grande];
            Ui.Responder(Dialogo.Aceptar);
            await Click(f, "OnPasteAttachmentClick");
            Assert.Equal(2, (await datos.Repo.GetAttachmentsAsync(tarea.Id)).Count);

            // Solo el grande: no se añade nada.
            Ui.Sistema.Ficheros = [grande];
            Ui.Responder(Dialogo.Aceptar);
            await Click(f, "OnPasteAttachmentClick");
            Assert.Equal(2, (await datos.Repo.GetAttachmentsAsync(tarea.Id)).Count);

            // Una imagen enorme tampoco.
            Ui.Sistema.Ficheros = null;
            var ruido = new byte[2000 * 2000 * 4];
            new Random(7).NextBytes(ruido);
            Ui.Sistema.Imagen = BitmapSource.Create(2000, 2000, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, ruido, 8000);
            Ui.Responder(Dialogo.Aceptar);
            await Click(f, "OnPasteAttachmentClick");
            Assert.Equal(2, (await datos.Repo.GetAttachmentsAsync(tarea.Id)).Count);

            // Y si algo revienta por el camino, se dice.
            Ui.Sistema.Imagen = null;
            Ui.Sistema.Ficheros = [fichero];
            Ui.Sistema.FalloPortapapeles = new IOException("portapapeles ocupado");
            Ui.Responder(Dialogo.Aceptar);
            await Click(f, "OnPasteAttachmentClick");
            Assert.Contains("portapapeles ocupado", Dialogo.Leidos.Last());
        });
    });

    [Fact]
    public Task Borrar_una_tarea_suelta_o_de_una_serie() => Ui.Run(async () =>
    {
        var (datos, lista, tarea) = await TareaAsync();

        // Suelta: cancelar y despues aceptar.
        var ficha = await Ficha(datos, tarea, async f =>
        {
            Ui.Responder(Dialogo.Primero);
            await Click(f, "OnDeleteClick");
            Assert.True(f.IsVisible);
            Ui.Responder(Dialogo.Aceptar);
            await Click(f, "OnDeleteClick");
        });
        Assert.True(ficha.Deleted);
        Assert.Null(await datos.Repo.GetTaskAsync(tarea.Id));

        // De una serie: cerrar no borra; «solo esta» y «la serie entera».
        var regar = await datos.Repo.AddTaskAsync(lista.Id, "Regar");
        regar.PlannedFor = DateTime.Today;
        regar.DueAt = DateTime.Today.AddDays(4);
        regar.RecurrenceRule = new Recurrence(RecurrenceKind.Daily, 1, 0, 0, 0).Serialize();
        await datos.Repo.UpdateTaskAsync(regar);
        await datos.Tasks.GenerateSeriesAsync(regar);
        regar = (await datos.Repo.GetTaskAsync(regar.Id))!;
        var serie = regar.SeriesId!.Value;
        var total = (await datos.Repo.GetSeriesAsync(serie)).Count;

        await Ficha(datos, regar, async f =>
        {
            Ui.Responder(Dialogo.Cerrar);
            await Click(f, "OnDeleteClick");
            Ui.Responder(Dialogo.Elegir(0));
            await Click(f, "OnDeleteClick");
        });
        Assert.Equal(total - 1, (await datos.Repo.GetSeriesAsync(serie)).Count);

        var otra = (await datos.Repo.GetSeriesAsync(serie))[0];
        await Ficha(datos, otra, async f =>
        {
            Ui.Responder(Dialogo.Elegir(1));
            await Click(f, "OnDeleteClick");
        });
        Assert.Empty(await datos.Repo.GetSeriesAsync(serie));
    });

    [Fact]
    public Task Las_filas_de_adjunto_sin_direccion_valida_no_rompen() => Ui.Run(async () =>
    {
        var (datos, _, tarea) = await TareaAsync();
        await datos.Repo.AddLinkAsync(tarea.Id, "http://[mal", "roto");
        await datos.Repo.AddLinkAsync(tarea.Id, "https://ok.es", "https://ok.es");

        await Ficha(datos, tarea, async f =>
        {
            await Ui.Hasta(() => f.AttachmentsBox.Items.Count == 2);
            var roto = f.AttachmentsBox.Items.Cast<object>().Single(a => Leer(a, "Name") == "roto");
            Assert.Null(roto.GetType().GetProperty("Link")!.GetValue(roto));
            Assert.Equal("http://[mal", Leer(roto, "Caption"));
        });

        // Una tarea en una lista que ya no existe cae en la primera.
        var huerfana = new TaskItem { Title = "Huerfana", ListId = Guid.NewGuid(), AccountId = "cuenta-a" };
        await Ficha(datos, huerfana, f =>
        {
            Assert.Equal(0, f.ListBox.SelectedIndex);
            return Task.CompletedTask;
        });
    });
}
