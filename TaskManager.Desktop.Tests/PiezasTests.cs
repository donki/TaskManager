using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TaskManager.Desktop.Controls;
using TaskManager.Desktop.Localization;
using TaskManager.Desktop.Tests.Banco;

namespace TaskManager.Desktop.Tests;

/// <summary>Las piezas sueltas: idioma de HandyControl y de WPF, el confeti y el icono que gira.</summary>
public class PiezasTests
{
    [Fact]
    public Task HandyControl_habla_el_idioma_de_la_aplicacion() => Ui.Run(async () =>
    {
        await Ui.Datos("es");
        HandyControlLang.Install();
        HandyControlLang.Install();

        var lang = typeof(HandyControl.Controls.Growl).Assembly.GetType("HandyControl.Properties.Langs.Lang")!;
        var gestor = (System.Resources.ResourceManager)lang.GetProperty("ResourceManager")!.GetValue(null)!;
        Assert.Equal("Cancelar", gestor.GetString("Cancel"));
        Assert.Equal("Sí", gestor.GetString("Yes", CultureInfo.InvariantCulture));

        // Lo que no esta traducido se le pide al original.
        Assert.Null(gestor.GetString("NoExisteEstaClave"));

        await Ui.Datos("en");
        Assert.Equal("Cancel", gestor.GetString("Cancel"));
        Assert.Equal("OK", gestor.GetString("Confirm"));
    });

    [Fact]
    public Task Los_calendarios_de_WPF_salen_en_el_idioma_de_la_aplicacion() => Ui.Run(async () =>
    {
        await Ui.Datos("es");
        WpfCulture.Install();
        Assert.Equal("es-ES", CultureInfo.CurrentCulture.Name);

        // Un selector de fecha recien cargado, y el calendario de su desplegable.
        var selector = new DatePicker();
        var ventana = await Ui.Mostrar(new Window { Content = new StackPanel { Children = { selector, new System.Windows.Controls.Calendar() } } });
        Assert.Equal("es-es", selector.Language.IetfLanguageTag.ToLowerInvariant());
        Assert.Equal("es-es", ventana.Language.IetfLanguageTag.ToLowerInvariant());

        selector.IsDropDownOpen = true;
        await Ui.Calma();
        selector.IsDropDownOpen = false;

        // Al cambiar de idioma, las ventanas abiertas se cambian solas.
        await Ui.Datos("en");
        WpfCulture.Install();
        Assert.Equal("en-US", CultureInfo.CurrentCulture.Name);
        Assert.Equal("en-us", ventana.Language.IetfLanguageTag.ToLowerInvariant());

        // Un elemento que no es de WPF no se toca.
        await Ui.Hacer(() => ventana.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, new FrameworkContentElement())));
    });

    [Fact]
    public Task La_marca_de_texto_del_XAML_devuelve_la_cadena() => Ui.Run(async () =>
    {
        await Ui.Datos("es");
        Assert.Equal(Loc.Get("Cancel"), new TExtension("Cancel").ProvideValue(null!));
        Assert.Equal(string.Empty, new TExtension().Key);
        Assert.Same(Loc.Texts, Loc.Texts);
    });

    [Fact]
    public Task El_confeti_cae_y_se_recoge_solo() => Ui.Run(async () =>
    {
        var confeti = new ConfettiHost { Width = 300, Height = 300 };
        await Ui.Mostrar(new Window { Content = confeti, Width = 320, Height = 320 });
        Assert.False(confeti.IsHitTestVisible);

        confeti.Burst(new Point(150, 100), 0.1);
        Assert.Equal(12, confeti.Children.Count);
        confeti.Burst(new Point(150, 100), 10);
        Assert.Equal(72, confeti.Children.Count);

        // Cada fotograma las mueve; al acabarse la vida desaparecen y se suelta el reloj.
        for (var i = 0; i < 300 && confeti.Children.Count > 0; i++)
        {
            Ui.Invocar(confeti, "OnRendering", null, EventArgs.Empty);
        }

        Assert.Empty(confeti.Children);
        Assert.False(Ui.Campo<bool>(confeti, "_running"));

        // Y con el reloj de verdad tambien se van.
        confeti.Burst(new Point(10, 10));
        await Ui.Hasta(() => confeti.Children.Count == 0, 20000, "que caiga el confeti");
    });

    [Fact]
    public Task El_icono_de_refrescar_gira_y_se_queda_derecho() => Ui.Run(() =>
    {
        var boton = new Button();
        Spinner.Start(boton);
        var giro = Assert.IsType<RotateTransform>(boton.RenderTransform);
        Assert.Equal(new Point(0.5, 0.5), boton.RenderTransformOrigin);

        Spinner.Start(boton);
        Assert.Same(giro, boton.RenderTransform);

        Spinner.Stop(boton);
        Assert.Equal(0, giro.Angle);
        Assert.False(giro.HasAnimatedProperties);
        return Task.CompletedTask;
    });

    /// <summary>
    /// Del sistema de verdad solo se prueba lo que lee sin cambiar nada: el teclado, el raton, si
    /// hay algo en el portapapeles y una clave del registro que existe en todo Windows. Escribir en
    /// el registro, abrir el navegador o pisar el portapapeles no se hace nunca desde las pruebas.
    /// </summary>
    [Fact]
    public Task El_sistema_de_verdad_lee_sin_tocar_nada() => Ui.Run(async () =>
    {
        var real = new Services.SistemaReal();
        Assert.True(real.BandejaVisible);
        _ = real.Teclas;

        var boton = new Button();
        await Ui.Mostrar(new Window { Content = boton });
        var raton = new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
        {
            RoutedEvent = UIElement.MouseMoveEvent,
        };
        Assert.True(Enum.IsDefined(real.BotonIzquierdo(raton)));
        _ = real.PosicionRaton(raton);

        // La clave del tema existe en cualquier Windows moderno; una que no existe da nulo.
        _ = real.LeerRegistro(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme");
        Assert.Null(real.LeerRegistro($@"Software\Socratic\NoExiste-{Guid.NewGuid():N}", "x"));

        // Borrar en una clave que no existe no hace nada (y no la crea).
        var inexistente = $@"Software\Socratic\NoExiste-{Guid.NewGuid():N}";
        real.BorrarRegistro(inexistente, "x");
        Assert.Null(Microsoft.Win32.Registry.CurrentUser.OpenSubKey(inexistente));

        // Soltar un atajo que nunca se cogio no hace nada.
        real.SoltarAtajo(IntPtr.Zero, 0x7FFF);

        // Mirar si hay algo en el portapapeles no lo cambia (otro programa puede tenerlo cogido).
        try
        {
            _ = real.HayTexto();
            _ = real.HayImagen();
            _ = real.HayFicheros();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }
    });

    [Fact]
    public Task Arrastrar_decide_por_el_umbral_y_el_destino() => Ui.Run(() =>
    {
        var filas = new List<string> { "a", "b", "c" };
        Assert.Equal((0, 2), Arrastre.Destino(filas, "a", null));
        Assert.Equal((2, 0), Arrastre.Destino(filas, "c", "a"));
        Assert.Null(Arrastre.Destino(filas, "c", null));
        Assert.Null(Arrastre.Destino(filas, "x", "a"));
        Assert.Null(Arrastre.Destino(filas, "a", "x"));
        Assert.Null(Arrastre.Fila(null));
        Assert.Null(Arrastre.Contenido<string>(new Button()));
        return Task.CompletedTask;
    });
}
