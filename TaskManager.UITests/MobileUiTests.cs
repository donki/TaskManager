using System.Drawing;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Enums;
using OpenQA.Selenium.Interactions;

namespace TaskManager.UITests;

/// <summary>
/// Tanda corta de interfaz del movil. Van en orden (T01, T02...) sobre la misma sesion: la primera
/// entra sin cuenta en una app recien borrada y las demas parten de «Mis tareas».
/// </summary>
[Collection(UiCollection.Name)]
[TestCaseOrderer("TaskManager.UITests.ByNameOrderer", "TaskManager.UITests")]
public sealed class MobileUiTests
{
    private readonly UiSession _s;

    public MobileUiTests(UiSession session) => _s = session;

    /// <summary>Entradas del menu lateral visibles (el correo esta apagado por FeatureOptions).</summary>
    private static readonly string[] MenuEntries =
        ["MenuMyTasks", "MenuCalendar", "MenuLists", "MenuKanban", "MenuGroups", "MenuSettings", "MenuWhatsNew", "MenuAbout"];

    [Fact]
    public void T01_Arranca_sin_cuenta_y_llega_a_Mis_tareas()
    {
        // App recien borrada: primero la puerta de entrada, y se sigue sin cuenta.
        _s.Require(UiSession.Id("LoginLocal"), "el boton «Seguir sin cuenta»", 30).Click();
        _s.Require(UiSession.Id("QuickAdd"), "la captura rapida de Mis tareas", 15);
        Assert.NotNull(_s.WaitFor(TitleOf("MenuMyTasks"), 5));
        _s.Shot("01-mis-tareas");
    }

    [Fact]
    public void T02_El_menu_lateral_abre_cada_entrada()
    {
        GoHome();
        foreach (var entry in MenuEntries)
        {
            OpenMenu();
            _s.Shot($"02-menu-{entry}");
            _s.Require(UiSession.Id(entry), $"la entrada {entry} del menu").Click();
            Assert.True(_s.WaitFor(TitleOf(entry), 8) is not null, $"{entry}: no sale su titulo en la cabecera.");
            Assert.Null(_s.WaitFor(UiSession.Id("MenuAbout"), 0.5)); // el menu se ha cerrado
            _s.Shot($"02-pagina-{entry}");
        }
    }

    [Fact]
    public void T03_Atras_segun_Mobile_7()
    {
        GoHome();

        // Fuera de inicio, atras vuelve a Mis tareas.
        OpenMenu();
        _s.Require(UiSession.Id("MenuCalendar"), "Calendario").Click();
        _s.Require(TitleOf("MenuCalendar"), "el titulo de Calendario");
        _s.Back();
        _s.Require(UiSession.Id("QuickAdd"), "Mis tareas tras atras desde Calendario");

        // Con el menu abierto, atras solo lo cierra.
        OpenMenu();
        _s.Back();
        Assert.Null(_s.WaitFor(UiSession.Id("MenuAbout"), 1.5));
        Assert.NotNull(_s.WaitFor(UiSession.Id("QuickAdd"), 3));

        // Desde el detalle de una tarea, atras vuelve a la lista.
        var title = AddTask("Atras");
        _s.Require(UiSession.Text(title), "la tarea recien creada").Click();
        _s.Require(UiSession.Id("TaskDelete"), "el detalle de la tarea");
        _s.Back();
        _s.Require(UiSession.Id("QuickAdd"), "Mis tareas tras atras desde el detalle");
        DeleteTask(title);

        // En inicio, atras oculta la app (no la cierra).
        _s.Back();
        Thread.Sleep(1500);
        Assert.Equal(AppState.RunningInBackground, _s.State());
        _s.Driver.ActivateApp(UiSession.Package);
        _s.Require(UiSession.Id("QuickAdd"), "Mis tareas al volver");
        _s.Shot("03-atras-vuelve");
    }

    [Fact]
    public void T04_Cambio_de_idioma_es_en()
    {
        foreach (var lang in new[] { "en", "es" })
        {
            GoHome();
            OpenMenu();
            _s.Require(UiSession.Id("MenuAbout"), "Acerca de").Click();
            _s.Require(UiSession.Id(lang == "es" ? "LanguageEs" : "LanguageEn"), $"el boton de {lang}", 10).Click();

            // Se rehace el Shell y se vuelve a Mis tareas, ya en el idioma elegido.
            _s.Require(TitleOf("MenuMyTasks", lang), $"«{UiSession.T("MenuMyTasks", lang)}»", 10);
            OpenMenu();
            foreach (var entry in MenuEntries)
            {
                Assert.True(_s.WaitFor(UiSession.Text(UiSession.T(entry, lang)), 2) is not null,
                    $"{lang}: en el menu no sale «{UiSession.T(entry, lang)}».");
            }

            _s.Shot($"04-menu-{lang}");
            _s.Back();
        }
    }

    [Fact]
    public void T05_Crear_y_borrar_una_tarea()
    {
        GoHome();
        var title = AddTask("Crear");
        _s.Shot("05-creada");
        DeleteTask(title);
        Assert.Null(_s.WaitFor(UiSession.Text(title), 2));
        _s.Shot("05-borrada");
    }

    /// <summary>
    /// Con la letra al 145 % ningun texto de «Mis tareas» ni del menu queda cortado.
    /// </summary>
    /// <remarks>
    /// Desde fuera no se puede medir lo que ocupa un texto, asi que se compara cada texto con el
    /// mismo a letra 1.0: al 145 % tiene que crecer ~1.45 en alto (o partirse en mas lineas). Si
    /// crece menos en alto, lo esta recortando su contenedor; si no crece en ancho y tampoco gana
    /// lineas, esta cortado (elipsis o recorte). Tambien falla un texto que se salga de la pantalla.
    /// </remarks>
    [Fact]
    public void T06_Letra_grande_sin_textos_cortados()
    {
        GoHome();
        AddTask("Letra"); // una fila de verdad en la lista, no solo el aviso de lista vacia
        var normal = Measure("normal");

        try
        {
            _s.SetFontScale(1.45);
            _s.Restart();
            _s.Require(UiSession.Id("QuickAdd"), "Mis tareas con la letra grande", 20);
            var big = Measure("letra145");

            var screen = _s.Driver.Manage().Window.Size;
            var problems = new List<string>();
            foreach (var (key, b) in big)
            {
                if (b.Right > screen.Width + 1 || b.Bottom > screen.Height + 1)
                {
                    problems.Add($"«{key}» se sale de la pantalla ({b}).");
                }

                if (key.Contains('…'))
                {
                    problems.Add($"«{key}» lleva elipsis.");
                }

                if (!normal.TryGetValue(key, out var n) || n.Height == 0 || n.Width == 0)
                {
                    continue;
                }

                var hRatio = (double)b.Height / n.Height;
                var wRatio = (double)b.Width / n.Width;
                var moreLines = hRatio > 1.9;
                if (hRatio < 1.25)
                {
                    problems.Add($"«{key}» no crece en alto (x{hRatio:0.00}): lo recorta su contenedor.");
                }
                else if (!moreLines && wRatio < 1.25 && b.Right < screen.Width - 2)
                {
                    // No crece en ancho, no gana lineas y aun le quedaba sitio: cortado.
                    problems.Add($"«{key}» no crece en ancho (x{wRatio:0.00}) ni parte en lineas: cortado.");
                }
                else if (!moreLines && wRatio < 1.25)
                {
                    problems.Add($"«{key}» llega al borde sin partir en lineas (x{wRatio:0.00}): cortado.");
                }
            }

            File.WriteAllLines(Path.Combine(_s.ArtifactsDir, "06-letra145.txt"),
                big.Select(kv => $"{kv.Key}\t1.0={(normal.TryGetValue(kv.Key, out var n) ? n.ToString() : "-")}\t1.45={kv.Value}")
                   .Concat(["", "PROBLEMAS:"]).Concat(problems));
            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }
        finally
        {
            _s.SetFontScale(1.0);
            _s.Restart();
            _s.Require(UiSession.Id("QuickAdd"), "Mis tareas con la letra normal", 20);
            foreach (var row in _s.Driver.FindElements(UiSession.TextContains("Prueba UI Letra")).Select(e => e.Text).ToList())
            {
                DeleteTask(row);
            }
        }
    }

    // ------------------------------------------------------------------ pasos comunes

    /// <summary>Textos visibles de «Mis tareas» y del menu, con sus limites en pantalla.</summary>
    private Dictionary<string, Rectangle> Measure(string shot)
    {
        var result = new Dictionary<string, Rectangle>();
        void Collect()
        {
            foreach (var e in _s.Driver.FindElements(By.ClassName("android.widget.TextView")))
            {
                var text = e.Text;
                if (string.IsNullOrWhiteSpace(text) || result.ContainsKey(text))
                {
                    continue;
                }

                result[text] = e.Rect;
            }
        }

        Collect();
        _s.Shot($"06-{shot}-mis-tareas");
        OpenMenu();
        Collect();
        _s.Shot($"06-{shot}-menu");
        _s.Back();
        _s.Require(UiSession.Id("QuickAdd"), "Mis tareas tras cerrar el menu");
        return result;
    }

    /// <summary>La cabecera de la pagina: el titulo en el idioma que este puesto.</summary>
    private static By TitleOf(string key, string? language = null)
    {
        if (language is not null)
        {
            return UiSession.Text(UiSession.T(key, language));
        }

        var es = UiSession.T(key, "es").Replace("'", "");
        var en = UiSession.T(key, "en").Replace("'", "");
        return By.XPath($"//android.widget.TextView[@text=\"{es}\" or @text=\"{en}\"]");
    }

    /// <summary>Vuelve a «Mis tareas» por el menu, desde donde se este.</summary>
    private void GoHome()
    {
        if (_s.State() != AppState.RunningInForeground)
        {
            _s.Driver.ActivateApp(UiSession.Package);
        }

        if (_s.WaitFor(UiSession.Id("QuickAdd"), 3) is not null)
        {
            return;
        }

        if (_s.WaitFor(UiSession.Id("LoginLocal"), 1) is { } local)
        {
            local.Click();
        }
        else
        {
            OpenMenu();
            _s.Require(UiSession.Id("MenuMyTasks"), "Mis tareas en el menu").Click();
        }

        _s.Require(UiSession.Id("QuickAdd"), "Mis tareas", 10);
    }

    /// <summary>
    /// Abre el menu lateral con el boton de la cabecera (el primero de la barra) y, si no lo
    /// encuentra, deslizando desde el borde izquierdo.
    /// </summary>
    private void OpenMenu()
    {
        var toggle = _s.WaitFor(By.XPath(
            "//*[@content-desc='Open navigation drawer' or @content-desc='Abrir panel lateral de navegación' or @content-desc='Abrir panel de navegación']"), 2)
            ?? _s.WaitFor(By.XPath("//android.view.ViewGroup/android.widget.ImageButton[1]"), 1);
        if (toggle is not null)
        {
            toggle.Click();
        }
        else
        {
            var size = _s.Driver.Manage().Window.Size;
            var finger = new PointerInputDevice(PointerKind.Touch);
            var swipe = new ActionSequence(finger);
            swipe.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, 2, size.Height / 2, TimeSpan.Zero));
            swipe.AddAction(finger.CreatePointerDown(MouseButton.Left));
            swipe.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, size.Width * 2 / 3, size.Height / 2, TimeSpan.FromMilliseconds(250)));
            swipe.AddAction(finger.CreatePointerUp(MouseButton.Left));
            _s.Driver.PerformActions([swipe]);
        }

        _s.Require(UiSession.Id("MenuAbout"), "el menu lateral abierto", 5);
    }

    /// <summary>Crea una tarea con la captura rapida y devuelve su titulo.</summary>
    private string AddTask(string tag)
    {
        var title = $"Prueba UI {tag} {DateTime.Now:HHmmss}";
        var entry = _s.Require(UiSession.Id("QuickAdd"), "la captura rapida");
        entry.Click();
        entry.SendKeys(title);
        _s.Require(UiSession.Id("QuickAddButton"), "el boton de añadir").Click();
        if (_s.Driver.IsKeyboardShown())
        {
            _s.Driver.HideKeyboard();
        }

        _s.Require(UiSession.Text(title), $"la tarea «{title}» en la lista", 10);
        return title;
    }

    /// <summary>Abre la tarea, pulsa la papelera de la cabecera y confirma.</summary>
    private void DeleteTask(string title)
    {
        _s.Require(UiSession.Text(title), $"la tarea «{title}»").Click();
        _s.Require(UiSession.Id("TaskDelete"), "la papelera del detalle", 10).Click();
        var confirm = _s.WaitFor(UiSession.Text(UiSession.T("Delete", "es")), 5)
            ?? _s.Require(UiSession.Text(UiSession.T("Delete", "en")), "el boton Borrar/Delete del dialogo", 2);
        confirm.Click();
        _s.Require(UiSession.Id("QuickAdd"), "Mis tareas tras borrar", 10);
        Assert.Null(_s.WaitFor(UiSession.Text(title), 2));
    }
}
