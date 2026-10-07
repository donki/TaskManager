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
        SettleHome();
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
            var id = lang == "es" ? "LanguageEs" : "LanguageEn";
            _s.Require(UiSession.ScrollTo(id), $"el boton de {lang}", 10).Click();

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
    /// Desde fuera no se ve la elipsis (el atributo text sigue siendo el texto entero) ni se puede
    /// medir lo que el texto querria ocupar. Lo que si se ve es el rectangulo visible de cada texto,
    /// que Android recorta a lo que deja ver su contenedor. Se compara con el mismo texto a letra
    /// 1.0 y:
    /// <list type="bullet">
    /// <item>FALLA si se sale de la pantalla, o si su parte visible es MENOR que con la letra normal
    ///   fuera de una zona desplazable (lo esta tapando o recortando su contenedor).</item>
    /// <item>AVISA (solo en el informe de artefactos) si crece en alto menos de x1.2: su fila no le
    ///   deja crecer y puede estar comiendose el aire de la letra. Dentro de una zona desplazable no
    ///   cuenta: puede ser solo que el texto queda al borde y se ve al desplazar.</item>
    /// </list>
    /// El ancho no se compara: un texto que llena su columna mide lo mismo con cualquier letra.
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
            var warnings = new List<string>();
            foreach (var (key, (b, inScroll)) in big)
            {
                if (b.Right > screen.Width + 1 || b.Bottom > screen.Height + 1 || b.Left < 0)
                {
                    problems.Add($"«{key}» se sale de la pantalla ({b}).");
                    continue;
                }

                if (!normal.TryGetValue(key, out var n) || n.Rect.Height == 0 || inScroll)
                {
                    continue;
                }

                var hRatio = (double)b.Height / n.Rect.Height;
                if (hRatio < 0.98)
                {
                    problems.Add($"«{key}» se ve menos que con la letra normal (alto x{hRatio:0.00}): lo recorta su contenedor.");
                }
                else if (hRatio < 1.2)
                {
                    warnings.Add($"«{key}» apenas crece en alto (x{hRatio:0.00}): su fila no le deja sitio.");
                }
            }

            File.WriteAllLines(Path.Combine(_s.ArtifactsDir, "06-letra145.txt"),
                big.Select(kv => $"{kv.Key}	1.0={(normal.TryGetValue(kv.Key, out var n) ? n.Rect.ToString() : "-")}	1.45={kv.Value.Rect}{(kv.Value.InScroll ? "	(desplazable)" : "")}")
                   .Concat(["", "FALLOS:"]).Concat(problems)
                   .Concat(["", "AVISOS:"]).Concat(warnings));
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

    /// <summary>
    /// Textos visibles de «Mis tareas» y del menu: su rectangulo visible y si estan dentro de algo
    /// desplazable.
    /// </summary>
    private Dictionary<string, (Rectangle Rect, bool InScroll)> Measure(string shot)
    {
        var result = new Dictionary<string, (Rectangle, bool)>();

        // Se lee el arbol entero de una vez (page source): pedir elemento a elemento es lento y
        // con listas se queda en los primeros.
        void Collect()
        {
            var xml = System.Xml.Linq.XDocument.Parse(_s.Driver.PageSource);
            foreach (var node in xml.Descendants())
            {
                var text = (string?)node.Attribute("text");
                var bounds = (string?)node.Attribute("bounds");
                if ((string?)node.Attribute("class") != "android.widget.TextView"
                    || (string?)node.Attribute("package") != UiSession.Package
                    || string.IsNullOrWhiteSpace(text) || bounds is null || result.ContainsKey(text))
                {
                    continue;
                }

                var n = System.Text.RegularExpressions.Regex.Matches(bounds, @"\d+").Select(m => int.Parse(m.Value)).ToArray();
                var inScroll = node.Ancestors().Any(a => (string?)a.Attribute("scrollable") == "true");
                result[text] = (Rectangle.FromLTRB(n[0], n[1], n[2], n[3]), inScroll);
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

    /// <summary>
    /// En el detalle, las etiquetas que ya existen salen todas, en varias filas y sin desplazar de
    /// lado: ninguna pastilla se sale de la pantalla por la derecha.
    /// </summary>
    [Fact]
    public void T07_Etiquetas_del_detalle_en_filas_sin_desplazar()
    {
        GoHome();
        var tags = Enumerable.Range(1, 14).Select(i => $"e2e{i:00}").ToList();
        var con = AddTask("Etiquetas");
        SetTags(con, tags);
        var otra = AddTask("Sin etiquetas");

        _s.Require(UiSession.Text(otra), "la segunda tarea").Click();
        _s.Require(UiSession.ScrollTo("TaskTags"), "el cuadro de etiquetas", 10);

        // La ultima puede quedar por debajo: se baja (en vertical) hasta verla.
        _s.Require(MobileBy.AndroidUIAutomator(
            "new UiScrollable(new UiSelector().scrollable(true).instance(0))" +
            $".scrollIntoView(new UiSelector().text(\"#{tags[^1]}\"))"), "la ultima etiqueta", 10);
        _s.Shot("07-etiquetas-en-filas");

        var width = _s.Driver.Manage().Window.Size.Width;
        var rows = new HashSet<int>();
        foreach (var tag in tags.TakeLast(6))
        {
            var chip = _s.Require(UiSession.Text($"#{tag}"), $"la pastilla #{tag}", 3);
            var r = chip.Rect;
            Assert.True(r.X >= 0 && r.Right <= width, $"#{tag} se sale de la pantalla ({r})");
            rows.Add(r.Y);
        }

        Assert.True(rows.Count > 1, "las etiquetas tendrian que ir en mas de una fila");

        _s.Back();
        _s.Require(UiSession.Id("QuickAdd"), "Mis tareas tras el detalle", 10);
        DeleteTask(con);
        DeleteTask(otra);
    }

    /// <summary>
    /// Ctrl+clic en el filtro de etiquetas suma una a la que ya habia; un clic normal deja solo
    /// esa. Ctrl se pulsa como un teclado fisico (acciones W3C: tecla abajo, toque, tecla arriba).
    /// </summary>
    [Fact]
    public void T08_Ctrl_clic_en_el_filtro_marca_varias_etiquetas()
    {
        GoHome();
        var a = AddTask("Filtro A");
        var b = AddTask("Filtro B");
        var c = AddTask("Filtro C");
        SetTags(a, ["e2ea"]);
        SetTags(b, ["e2eb"]);

        try
        {
            Chip("#e2ea").Click();
            _s.Require(UiSession.Text(a), "A con su etiqueta", 5);
            Assert.Null(_s.WaitFor(UiSession.Text(b), 1));

            var chip = Chip("#e2eb");
            new Actions(_s.Driver).KeyDown(Keys.Control).Click(chip).KeyUp(Keys.Control).Perform();
            _s.Require(UiSession.Text(b), "B tras Ctrl+clic", 5);
            Assert.NotNull(_s.WaitFor(UiSession.Text(a), 2));
            Assert.Null(_s.WaitFor(UiSession.Text(c), 1));
            _s.Shot("08-ctrl-clic-dos-etiquetas");

            // Un clic normal deja solo esa; «Todas» lo suelta.
            Chip("#e2eb").Click();
            Assert.Null(_s.WaitFor(UiSession.Text(a), 2));
        }
        finally
        {
            // Pase lo que pase se suelta el filtro: si no, las pruebas siguientes no ven sus tareas.
            Chip(UiSession.T("AllTags", "es"), UiSession.T("AllTags", "en")).Click();
        }

        _s.Require(UiSession.Text(c), "C sin filtro", 5);

        DeleteTask(a);
        DeleteTask(b);
        DeleteTask(c);
    }

    /// <summary>
    /// Las notas del detalle anuncian al teclado que admiten imagenes (image/*), que es lo que
    /// hace que «Pegar» con una imagen y las imagenes de Gboard lleguen a la aplicacion.
    /// </summary>
    /// <remarks>
    /// Aqui se comprueba, en el dispositivo, que el cuadro de texto real lo anuncia (lo que publica
    /// el sistema en <c>dumpsys input_method</c>); el pegado de una imagen de verdad es T10.
    /// </remarks>
    [Fact]
    public void T09_Las_notas_admiten_imagenes_pegadas()
    {
        GoHome();
        var title = AddTask("Pegar");
        _s.Require(UiSession.Text(title), "la tarea").Click();
        _s.Require(UiSession.ScrollTo("TaskNotes"), "las notas", 10).Click();
        Thread.Sleep(800);

        var dump = _s.Adb("shell dumpsys input_method");
        Assert.Contains("image/*", dump);
        _s.Shot("09-notas-con-imagenes");

        if (_s.Driver.IsKeyboardShown())
        {
            _s.Driver.HideKeyboard();
        }

        _s.Back();
        _s.Require(UiSession.Id("QuickAdd"), "Mis tareas tras el detalle", 10);
        DeleteTask(title);
    }

    /// <summary>
    /// Pegar una imagen de verdad: otra aplicacion (el ayudante TaskManager.UITests.Portapapeles)
    /// la deja en el portapapeles como un content:// suyo, igual que Chrome, Edge o Google Fotos, y
    /// se pega por cada camino del detalle: el boton de pegar adjunto y la tecla de pegar (la misma
    /// accion que «Pegar» del menu del texto) en el titulo, las notas, las etiquetas y el paso
    /// nuevo. Cada vez tiene que salir arriba el aviso «Imagen añadida…» y un adjunto mas.
    /// </summary>
    /// <remarks>
    /// El fallo del 2026-10-07: la imagen pegada en las notas si se guardaba, pero en «Enlaces y
    /// ficheros», fuera de la vista, y sin ningun aviso; parecia que pegar no hacia nada. La imagen
    /// del teclado (Gboard) no se puede mandar desde Appium: va por el mismo receptor que el pegado.
    /// </remarks>
    [Fact]
    public void T10_Pegar_una_imagen_copiada_en_otra_aplicacion()
    {
        _s.InstallClipHelper();
        GoHome();
        var title = AddTask("Imagen");
        _s.Require(UiSession.Text(title), "la tarea").Click();
        _s.Require(UiSession.Id("TaskDelete"), "el detalle", 10);

        var expected = 0;

        // 1) El boton de pegar adjunto (y con un proveedor que no dice el tipo).
        foreach (var withoutType in new[] { false, true })
        {
            _s.CopyImageToClipboard(withoutType);
            _s.Require(UiSession.ScrollTo("PasteAttachment"), "el boton de pegar adjunto", 10).Click();
            expected++;
            ExpectPastedNotice($"boton{(withoutType ? " sin tipo" : "")}");
        }

        _s.Shot("10-pegada-con-el-boton");

        // 2) La tecla de pegar en cada cuadro de texto del detalle.
        foreach (var box in new[] { "TaskWhat", "TaskNotes", "TaskTags", "NewStep" })
        {
            _s.CopyImageToClipboard();
            var field = _s.Require(UiSession.ScrollTo(box), $"el cuadro {box}", 10);
            field.Click();
            Thread.Sleep(500);
            _s.Adb("shell input keyevent 279");   // KEYCODE_PASTE
            expected++;
            ExpectPastedNotice(box);
            if (_s.Driver.IsKeyboardShown())
            {
                _s.Driver.HideKeyboard();
            }
        }

        _s.Shot("10-pegada-en-los-textos");
        Assert.Equal(expected, CountPastedAttachments());

        // Lo pegado no dejo texto en los cuadros: el titulo sigue siendo el mismo.
        Assert.Equal(title, _s.Require(UiSession.ScrollTo("TaskWhat"), "el titulo", 10).Text);

        _s.Back();
        if (_s.WaitFor(UiSession.Id("QuickAdd"), 3) is null)
        {
            // Si pregunta por cambios sin guardar, se descartan.
            var discard = _s.WaitFor(UiSession.Text(UiSession.T("Discard", "es")), 3)
                ?? _s.WaitFor(UiSession.Text(UiSession.T("Discard", "en")), 1);
            discard?.Click();
        }

        _s.Require(UiSession.Id("QuickAdd"), "Mis tareas tras el detalle", 10);
        DeleteTask(title);
        _s.UninstallClipHelper();
    }

    /// <summary>
    /// Espera el aviso de imagen añadida (arriba del detalle, en el idioma que este puesto) y a que
    /// se vaya, para no confundirlo con el del siguiente pegado.
    /// </summary>
    private void ExpectPastedNotice(string where)
    {
        var es = UiSession.T("PastedImageAdded", "es");
        var en = UiSession.T("PastedImageAdded", "en");
        var banner = _s.WaitFor(By.XPath($"//*[@text=\"{es}\" or @text=\"{en}\"]"), 5);
        if (banner is null)
        {
            _s.Shot($"10-sin-aviso-{where}");
        }

        Assert.True(banner is not null, $"Pegar en {where}: no sale el aviso de imagen añadida.");
        _s.Shot($"10-aviso-{where}");
        var until = DateTime.UtcNow.AddSeconds(6);
        while (DateTime.UtcNow < until && _s.Driver.FindElements(By.XPath($"//*[@text=\"{es}\" or @text=\"{en}\"]")).Count > 0)
        {
            Thread.Sleep(300);
        }
    }

    /// <summary>Cuenta los adjuntos «Imagen …» bajando por el detalle.</summary>
    private int CountPastedAttachments()
    {
        var es = UiSession.T("PastedImageName", "es") + " 2";
        var en = UiSession.T("PastedImageName", "en") + " 2";
        _s.Require(UiSession.ScrollTo("PasteAttachment"), "el boton de pegar adjunto", 10);
        var names = new HashSet<string>();
        var size = _s.Driver.Manage().Window.Size;
        for (var i = 0; i < 6; i++)
        {
            foreach (var e in _s.Driver.FindElements(By.XPath($"//*[starts-with(@text,'{es}') or starts-with(@text,'{en}')]")))
            {
                names.Add(e.Text);
            }

            _s.Adb($"shell input swipe {size.Width / 2} {size.Height * 3 / 4} {size.Width / 2} {size.Height / 2} 300");
            Thread.Sleep(500);
        }

        return names.Count;
    }

    /// <summary>
    /// Una pastilla del filtro de etiquetas: la tira se desplaza de lado y, con muchas etiquetas o
    /// la pantalla estrecha, la que se busca puede estar fuera; se desplaza hasta verla (la tira
    /// de etiquetas es la segunda HorizontalScrollView: la primera es la de los estados).
    /// </summary>
    private AppiumElement Chip(string text, string? other = null)
    {
        foreach (var t in other is null ? [text] : new[] { text, other })
        {
            if (_s.WaitFor(UiSession.Text(t), 1) is { } seen)
            {
                return seen;
            }
        }

        return _s.Require(MobileBy.AndroidUIAutomator(
            "new UiScrollable(new UiSelector().className(\"android.widget.HorizontalScrollView\").instance(1)).setAsHorizontalList()" +
            $".scrollIntoView(new UiSelector().text(\"{text}\"))"), $"la pastilla {text}", 10);
    }

    /// <summary>Abre la tarea, le escribe las etiquetas, guarda y vuelve a «Mis tareas».</summary>
    private void SetTags(string title, IEnumerable<string> tags)
    {
        _s.Require(UiSession.Text(title), $"la tarea «{title}»").Click();
        var box = _s.Require(UiSession.ScrollTo("TaskTags"), "el cuadro de etiquetas", 10);
        box.Click();
        box.Clear();
        box.SendKeys(string.Join(", ", tags));
        if (_s.Driver.IsKeyboardShown())
        {
            _s.Driver.HideKeyboard();
        }

        _s.Require(UiSession.Id("TaskSave"), "guardar").Click();
        if (_s.WaitFor(UiSession.Id("QuickAdd"), 5) is null)
        {
            _s.Back();
        }

        _s.Require(UiSession.Id("QuickAdd"), "Mis tareas tras guardar", 10);
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
            SettleHome();
        }
        else
        {
            OpenMenu();
            _s.Require(UiSession.Id("MenuMyTasks"), "Mis tareas en el menu").Click();
        }

        _s.Require(UiSession.Id("QuickAdd"), "Mis tareas", 10);
    }

    /// <summary>
    /// Tras entrar sin cuenta salen, una vez, el permiso de avisos de Android y la pantalla de
    /// Novedades de la version recien instalada: se acepta el permiso (es un emulador de pruebas)
    /// y se sale de Novedades con atras, que tiene que llevar a «Mis tareas» (Mobile 7).
    /// </summary>
    private void SettleHome()
    {
        var until = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < until)
        {
            if (_s.Driver.FindElements(By.Id("com.android.permissioncontroller:id/permission_allow_button")) is { Count: > 0 } allow)
            {
                allow[0].Click();
            }
            else if (_s.Driver.FindElements(UiSession.Id("QuickAdd")).Count > 0)
            {
                return;
            }
            else if (_s.Driver.FindElements(TitleOf("MenuWhatsNew")).Count > 0)
            {
                _s.Shot("00-novedades");
                _s.Back();
            }

            Thread.Sleep(400);
        }

        throw new Xunit.Sdk.XunitException("No se llega a «Mis tareas» tras entrar sin cuenta.");
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

        // Al añadir se abre el detalle de la tarea nueva; atras vuelve a la lista (Mobile 7).
        _s.Require(UiSession.Id("TaskDelete"), "el detalle de la tarea nueva", 10);
        if (_s.Driver.IsKeyboardShown())
        {
            _s.Driver.HideKeyboard();
        }

        _s.Back();
        _s.Require(UiSession.Id("QuickAdd"), "Mis tareas tras atras desde el detalle", 10);
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
