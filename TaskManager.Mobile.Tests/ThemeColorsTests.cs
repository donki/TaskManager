using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Models;
using TaskManager.Mobile.Pages;
using TaskManager.Mobile.Tests.Infra;

namespace TaskManager.Mobile.Tests;

/// <summary>
/// El tema elegido en Ajustes manda en TODOS los controles, aunque el del sistema sea el contrario
/// (Xiaomi con Android 16 en oscuro y la aplicacion en claro, 2026-11-16): el desplegable del tema
/// enseñaba texto blanco sobre la tarjeta blanca y los interruptores perdian el pulgar. Y ningun
/// emoji en pantalla: la chincheta de las fijadas es un icono plano (General 6.2).
/// </summary>
public class ThemeColorsTests
{
    private static Color Res(TestApp app, string key) => (Color)app.Application!.Resources[key];

    private static string Suffix(AppTheme theme) => theme == AppTheme.Dark ? "Dark" : "Light";

    /// <summary>
    /// Mete la pagina en una ventana de la aplicacion, como en el movil. El cambio de tema baja
    /// desde la ventana por el arbol de vistas: una pagina suelta se quedaria con el tema con el
    /// que se creo y la prueba no veria nada.
    /// </summary>
    private static async Task<T> ShowAsync<T>(TestApp app, T page) where T : Page
    {
        var window = new Window(page);
        typeof(Application)
            .GetMethod("AddWindow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(app.Application, [window]);
        await page.Appear();
        return page;
    }

    /// <summary>Todos los descendientes de un tipo, por el arbol logico de MAUI.</summary>
    private static List<T> All<T>(Element root) where T : Element
    {
        var found = new List<T>();
        void Walk(Element e)
        {
            if (e is T t)
            {
                found.Add(t);
            }

            foreach (var child in ((IElementController)e).LogicalChildren)
            {
                Walk(child);
            }
        }

        Walk(root);
        return found;
    }

    /// <summary>
    /// Los colores del texto y de los controles salen del tema de la aplicacion, en los dos sentidos,
    /// y cambian en caliente: la pagina se crea en un tema y se pasa al otro, que es justo lo que
    /// hace el usuario en Ajustes.
    /// </summary>
    private static void AssertThemed(TestApp app, Page page, AppTheme theme)
    {
        var s = Suffix(theme);
        var text = Res(app, $"TextPrimary{s}");
        var card = Res(app, $"CardBackground{s}");
        Assert.NotEqual(text, card);

        foreach (var picker in All<Picker>(page))
        {
            Assert.Equal(text, picker.TextColor);
            Assert.Equal(Res(app, $"TextSecondary{s}"), picker.TitleColor);
        }

        foreach (var picker in All<DatePicker>(page))
        {
            Assert.Equal(text, picker.TextColor);
        }

        foreach (var picker in All<TimePicker>(page))
        {
            Assert.Equal(text, picker.TextColor);
        }

        foreach (var editor in All<Editor>(page))
        {
            Assert.Equal(text, editor.TextColor);
            Assert.Equal(Res(app, $"TextSecondary{s}"), editor.PlaceholderColor);
        }

        foreach (var entry in All<Entry>(page))
        {
            Assert.Equal(text, entry.TextColor);
        }

        foreach (var box in All<CheckBox>(page))
        {
            Assert.Equal(Res(app, $"Check{s}"), box.Color);
        }

        foreach (var toggle in All<Switch>(page))
        {
            AssertSwitchVisible(app, toggle, theme);
        }
    }

    /// <summary>El pulgar se ve contra la tarjeta y contra su pista, encendido y apagado.</summary>
    private static void AssertSwitchVisible(TestApp app, Switch toggle, AppTheme theme)
    {
        var s = Suffix(theme);
        var card = Res(app, $"CardBackground{s}");
        var state = toggle.IsToggled ? "On" : "Off";

        Assert.Equal(Res(app, $"SwitchThumb{state}{s}"), toggle.ThumbColor);
        Assert.Equal(Res(app, $"SwitchTrackOn{s}"), toggle.OnColor);
        Assert.Equal(Res(app, $"SwitchTrackOff{s}"), toggle.OffColor);

        var track = toggle.IsToggled ? toggle.OnColor : toggle.OffColor;
        Assert.NotEqual(card, toggle.ThumbColor);
        Assert.NotEqual(track, toggle.ThumbColor);
        Assert.NotEqual(Colors.White, toggle.ThumbColor);
    }

    [Theory]
    [InlineData(AppTheme.Dark, 1, AppTheme.Light)]   // el caso del Xiaomi: venia de oscuro, elige claro
    [InlineData(AppTheme.Light, 2, AppTheme.Dark)]
    public void Ajustes_pinta_desplegables_e_interruptores_con_el_tema_elegido(AppTheme before, int choice, AppTheme after) =>
        UiThread.Run(async () =>
        {
            await using var app = await TestApp.StartAsync();
            app.Application!.UserAppTheme = before;
            var page = await ShowAsync(app, new SettingsPage());
            AssertThemed(app, page, before);

            var picker = page.Named<Picker>("ThemePicker");
            await PageDriver.Set(() => picker.SelectedIndex = choice);
            Assert.Equal(after, app.Application.UserAppTheme);

            // El valor elegido sigue ahi y se lee: texto del tema, no el color de la tarjeta.
            Assert.Equal(choice, picker.SelectedIndex);
            Assert.NotEqual(Res(app, $"CardBackground{Suffix(after)}"), picker.TextColor);
            AssertThemed(app, page, after);

            // Los interruptores de la celebracion, en los dos estados y en el tema nuevo.
            foreach (var name in new[] { "HapticsSwitch", "SoundSwitch" })
            {
                var toggle = page.Named<Switch>(name);
                await PageDriver.Set(() => toggle.IsToggled = true);
                AssertSwitchVisible(app, toggle, after);
                await PageDriver.Set(() => toggle.IsToggled = false);
                AssertSwitchVisible(app, toggle, after);
            }
        });

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void El_detalle_de_la_tarea_pinta_todos_sus_controles_con_el_tema(AppTheme theme) => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Comprar pan");
        var task = tasks[0];
        task.IsPinned = true;
        task.DueAt = DateTime.Today.AddDays(1);
        await app.Repository.UpdateTaskAsync(task);

        // Se abre en el tema contrario y se cambia con la pagina ya creada.
        app.Application!.UserAppTheme = theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        var page = await ShowAsync(app, new TaskDetailPage { TaskId = task.Id.ToString() });
        app.Application.UserAppTheme = theme;
        await UiThread.IdleAsync();

        Assert.NotEmpty(All<Picker>(page));
        Assert.NotEmpty(All<DatePicker>(page));
        Assert.NotEmpty(All<Editor>(page));
        Assert.Contains(All<Switch>(page), s => s.IsToggled);
        Assert.Contains(All<Switch>(page), s => !s.IsToggled);
        AssertThemed(app, page, theme);
    });

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void Mis_tareas_pinta_cajas_y_casillas_con_el_tema(AppTheme theme) => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        await app.SeedAsync("Casa", "Pan");
        app.Application!.UserAppTheme = theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        var page = await ShowAsync(app, new MyTasksPage());
        app.Application.UserAppTheme = theme;
        await UiThread.IdleAsync();
        AssertThemed(app, page, theme);
    });

    /// <summary>
    /// Cada plantilla de fila de tarea (Mis tareas, lista, calendario y tablero) pinta la chincheta
    /// como icono plano cuando la tarea esta fijada y la esconde cuando no, con el titulo limpio.
    /// </summary>
    [Fact]
    public void Las_filas_de_tarea_pintan_la_chincheta_como_icono_y_no_como_emoji() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (list, _) = await app.SeedAsync("Casa", "Pan");

        Page[] pages =
        [
            new MyTasksPage(),
            new ListDetailPage { ListId = list.Id.ToString() },
            new CalendarPage(),
            new KanbanPage(),
        ];

        var checkedTemplates = 0;
        foreach (var page in pages)
        {
            await page.Appear();
            var templates = All<CollectionView>(page).Select(c => c.ItemTemplate).OfType<DataTemplate>().ToList();
            foreach (var template in templates)
            {
                foreach (var pinned in new[] { true, false })
                {
                    if (template.CreateContent() is not View view)
                    {
                        continue;
                    }

                    var row = new TaskRow(new TaskItem { Title = "Pagar", IsPinned = pinned });
                    try
                    {
                        view.BindingContext = row;
                    }
                    catch (InvalidCastException)
                    {
                        continue;   // una plantilla de otra cosa (pasos, etiquetas)
                    }

                    var title = All<Label>(view).FirstOrDefault(l => l.Text == "Pagar");
                    if (title is null)
                    {
                        continue;
                    }

                    var pin = All<Image>(view).Single(i => i.Source is FileImageSource f && f.File == "ic_pin.png");
                    Assert.Equal(pinned, pin.IsVisible);
                    Assert.DoesNotContain(All<Label>(view), l => l.Text?.Contains("\U0001F4CC") == true);
                    checkedTemplates++;
                }
            }
        }

        // Las cuatro paginas, fijada y sin fijar.
        Assert.True(checkedTemplates >= 8, $"Solo se comprobaron {checkedTemplates} plantillas.");
    });

    [Fact]
    public void El_resumen_marca_los_pasos_con_el_circulo_de_las_listas() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (_, tasks) = await app.SeedAsync("Casa", "Mudanza");
        var steps = await app.Repository.AddStepsAsync(tasks[0].Id, ["Cajas", "Furgoneta"]);
        steps[0].IsDone = true;
        await app.Repository.UpdateStepAsync(steps[0]);

        var page = new TaskSummaryPage { TaskId = tasks[0].Id.ToString() };
        await page.Appear();

        var rows = page.Named<VerticalStackLayout>("StepsHost").Cast<Grid>().ToList();
        Assert.Equal(2, rows.Count);
        var icons = rows.Select(r => ((FileImageSource)r.Children.OfType<Image>().Single().Source).File).ToList();
        var titles = rows.Select(r => r.Children.OfType<Label>().Single().Text).ToList();
        Assert.Equal(["ic_circle_check.png", "ic_circle.png"], icons);
        Assert.Equal(["Cajas", "Furgoneta"], titles);
    });
}
