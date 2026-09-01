using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;

namespace TaskManager.Desktop.Controls;

/// <summary>
/// Los avisos y las preguntas de la aplicacion, con su mismo aspecto.
/// </summary>
/// <remarks>
/// <para><b>Por que no <see cref="MessageBox"/>.</b> El del sistema pinta un cuadro gris con
/// botones cuadrados y un icono de Windows 95: al lado de una ventana de tarjetas redondeadas e
/// indigo, canta. Y no sigue el tema oscuro. Es el equivalente de <c>SocShared.ModernDialog</c> del
/// movil, con las mismas piezas —tarjeta, titulo en indigo, texto atenuado y botones de accion— para
/// que preguntar lo mismo se vea igual en los dos sitios.</para>
///
/// <para>Es modal y bloquea, como <see cref="MessageBox"/>: quien pregunta «¿lo borro?» necesita la
/// respuesta antes de seguir.</para>
/// </remarks>
public static class ModernDialog
{
    /// <summary>
    /// Pregunta de si o no. Devuelve <c>true</c> si se acepta.
    /// </summary>
    /// <param name="danger">
    /// Si lo que se va a hacer no tiene vuelta atras. Pinta el boton de aceptar en rojo, que es lo
    /// unico que distingue «borrar» de «guardar» cuando se lee en diagonal.
    /// </param>
    public static bool Confirm(Window owner, string title, string message, bool danger = false)
    {
        var window = Build(owner, title, message, out var content);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };

        var cancel = IconButton(owner, "", Localization.Loc.Get("Cancel"), "GhostIconButton");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => window.DialogResult = false;

        var accept = IconButton(owner, danger ? "" : "",
            danger ? Localization.Loc.Get("Delete") : Localization.Loc.Get("Save"),
            danger ? "DangerIconButton" : "IconButton");
        accept.Click += (_, _) => window.DialogResult = true;

        buttons.Children.Add(cancel);
        buttons.Children.Add(accept);
        content.Children.Add(buttons);

        // Escape cancela y Enter acepta: en un cuadro de dos botones son los atajos que todo el
        // mundo prueba sin pensar.
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.DialogResult = false;
            }
            else if (e.Key == Key.Enter)
            {
                window.DialogResult = true;
            }
        };

        return window.ShowDialog() == true;
    }

    /// <summary>Un solo botón: enterarse y cerrar.</summary>
    public static void Alert(Window owner, string title, string message)
    {
        var window = Build(owner, title, message, out var content);

        var ok = IconButton(owner, "", "OK", "IconButton");
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.Margin = new Thickness(0, 18, 0, 0);
        ok.Click += (_, _) => window.DialogResult = true;

        content.Children.Add(ok);
        window.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Enter)
            {
                window.DialogResult = true;
            }
        };

        window.ShowDialog();
    }

    /// <summary>
    /// Elegir uno de varios, con una salida alternativa.
    /// </summary>
    /// <remarks>
    /// Se usa al borrar una lista que todavia tiene tareas: hay que decidir a donde van antes de
    /// que desaparezca la lista, porque <b>ninguna tarea puede quedarse sin lista</b>. La salida
    /// alternativa es «borrarlas tambien», que sigue siendo una respuesta valida.
    /// </remarks>
    /// <returns>
    /// Lo elegido; <c>null</c> si se escogio la salida alternativa. La cancelacion se distingue por
    /// <paramref name="cancelled"/>, que es lo unico que no debe hacer nada.
    /// </returns>
    public static T? Choose<T>(
        Window owner,
        string title,
        string message,
        IReadOnlyList<(string Label, T Value)> options,
        string alternativeLabel,
        out bool cancelled)
        where T : struct
    {
        var window = Build(owner, title, message, out var content);

        var list = new ListBox
        {
            Style = (Style)owner.FindResource("PickRows"),
            Margin = new Thickness(0, 14, 0, 0),
            MaxHeight = 220,
            ItemsSource = options.Select(o => o.Label).ToList(),
            SelectedIndex = 0,
        };

        content.Children.Add(list);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };

        var alternative = IconButton(owner, "\uE74D", alternativeLabel, "DangerIconButton");
        alternative.Margin = new Thickness(0, 0, 8, 0);

        var accept = IconButton(owner, "\uE8DE", Localization.Loc.Get("MoveThem"), "IconButton");

        var chosen = false;
        alternative.Click += (_, _) => window.DialogResult = true;
        accept.Click += (_, _) =>
        {
            chosen = true;
            window.DialogResult = true;
        };

        buttons.Children.Add(alternative);
        buttons.Children.Add(accept);
        content.Children.Add(buttons);

        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.DialogResult = false;
            }
        };

        // Cerrar la ventana sin pulsar nada NO es "borralas": es no hacer nada.
        cancelled = window.ShowDialog() != true;
        if (cancelled || !chosen)
        {
            return null;
        }

        var index = Math.Clamp(list.SelectedIndex, 0, options.Count - 1);
        return options[index].Value;
    }

    /// <summary>
    /// Elegir de una lista, sin salida alternativa. Devuelve <c>null</c> si se cerro sin elegir.
    /// </summary>
    public static T? Pick<T>(
        Window owner,
        string title,
        string message,
        IReadOnlyList<(string Label, T Value)> options,
        string acceptTooltip)
        where T : struct
    {
        var window = Build(owner, title, message, out var content);

        var list = new ListBox
        {
            Style = (Style)owner.FindResource("PickRows"),
            Margin = new Thickness(0, 14, 0, 0),
            MaxHeight = 260,
            ItemsSource = options.Select(o => o.Label).ToList(),
            SelectedIndex = 0,
        };

        content.Children.Add(list);

        var accept = IconButton(owner, "", acceptTooltip, "IconButton");
        accept.HorizontalAlignment = HorizontalAlignment.Right;
        accept.Margin = new Thickness(0, 16, 0, 0);
        accept.Click += (_, _) => window.DialogResult = true;
        content.Children.Add(accept);

        // Doble clic sobre una fila: elegir y cerrar. Es el gesto de siempre en una lista.
        list.MouseDoubleClick += (_, _) => window.DialogResult = true;

        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.DialogResult = false;
            }
        };

        if (window.ShowDialog() != true || list.SelectedIndex < 0)
        {
            return null;
        }

        return options[Math.Clamp(list.SelectedIndex, 0, options.Count - 1)].Value;
    }

    /// <summary>
    /// Elegir una de las que ya existen o escribir una nueva, en el mismo cuadro.
    /// </summary>
    /// <remarks>
    /// Para etiquetas. Separarlo en dos cuadros («¿usar una existente o crear una?») obligaria a
    /// decidir antes de ver la lista, que es justo al reves de como se decide esto: primero se mira
    /// si ya hay una que sirva y solo si no la hay se escribe.
    /// </remarks>
    public static string? PickOrType(
        Window owner,
        string title,
        string message,
        IReadOnlyList<string> options,
        string hint,
        string acceptTooltip)
    {
        var window = Build(owner, title, message, out var content);

        var list = new ListBox
        {
            Style = (Style)owner.FindResource("PickRows"),
            Margin = new Thickness(0, 14, 0, 0),
            MaxHeight = 200,
            ItemsSource = options.ToList(),
            SelectedIndex = -1,
            Visibility = options.Count == 0 ? Visibility.Collapsed : Visibility.Visible,
        };

        content.Children.Add(list);

        var box = new TextBox
        {
            Style = (Style)owner.FindResource("Field"),
            Margin = new Thickness(0, 12, 0, 0),
        };

        content.Children.Add(new TextBlock
        {
            Text = hint,
            Margin = new Thickness(0, 12, 0, 0),
            Style = (Style)owner.FindResource("HintText"),
        });
        content.Children.Add(box);

        // Escribir y tener ademas una fila marcada seria ambiguo: lo ultimo que se toca manda.
        box.TextChanged += (_, _) =>
        {
            if (box.Text.Length > 0)
            {
                list.SelectedIndex = -1;
            }
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex >= 0)
            {
                box.Clear();
            }
        };

        var accept = IconButton(owner, "", acceptTooltip, "IconButton");
        accept.HorizontalAlignment = HorizontalAlignment.Right;
        accept.Margin = new Thickness(0, 16, 0, 0);
        accept.Click += (_, _) => window.DialogResult = true;
        content.Children.Add(accept);

        list.MouseDoubleClick += (_, _) => window.DialogResult = true;
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                window.DialogResult = true;
            }
        };

        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.DialogResult = false;
            }
        };

        box.Loaded += (_, _) => box.Focus();

        if (window.ShowDialog() != true)
        {
            return null;
        }

        var written = box.Text.Trim();
        if (written.Length > 0)
        {
            return written;
        }

        return list.SelectedItem as string;
    }

    // -----------------------------------------------------------------------

    private static Window Build(Window owner, string title, string message, out StackPanel content)
    {
        content = new StackPanel();

        content.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)owner.FindResource("CardTitle"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        content.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)owner.FindResource("TextSecondary"),
        });

        var card = new Border
        {
            Style = (Style)owner.FindResource("Card"),
            Padding = new Thickness(20, 18, 20, 18),
            Margin = new Thickness(14),
            Child = content,
        };

        return new Window
        {
            // Sin barra de titulo: la tarjeta ES el cuadro, y una barra del sistema encima
            // devolveria justo el aspecto que se queria evitar.
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.Height,
            Width = 400,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = card,
        };
    }

    private static Button IconButton(Window owner, string glyph, string tooltip, string styleKey) =>
        new()
        {
            Content = glyph,
            ToolTip = tooltip,
            Style = (Style)owner.FindResource(styleKey),
        };
}
