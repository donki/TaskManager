using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskManager.Core.Models;
using TaskManager.Core.Services;

namespace TaskManager.Desktop;

/// <summary>
/// Una tarea de un vistazo, de solo lectura, con el lapiz aparte para editarla.
/// </summary>
/// <remarks>
/// <para>Lo pidio un probador el 2026-11-08: abrir una tarea para verla no tenia que ser entrar en
/// el formulario. El doble clic en una tarea abre esto; el lapiz de la fila (y el de aqui) abre el
/// detalle de siempre (<see cref="TaskDetailWindow"/>).</para>
///
/// <para>El contenido sale de <see cref="TaskSummary"/>, el mismo que usa Android: las dos
/// plataformas dicen lo mismo de una tarea.</para>
/// </remarks>
public sealed class TaskSummaryWindow : Window
{
    /// <summary>Se pulso el lapiz: quien abrio la ventana abre ahora el detalle.</summary>
    public bool EditRequested { get; private set; }

    /// <summary>El panel con las filas del resumen. Expuesto para las pruebas.</summary>
    internal StackPanel LinesPanel { get; } = new();

    internal TextBlock TitleText { get; }

    internal TextBlock BareHint { get; }

    internal StackPanel StepsPanel { get; } = new();

    internal TextBlock NotesText { get; }

    public TaskSummaryWindow(TaskSummary summary)
    {
        Title = T("TaskSummaryTitle");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 640;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("PageBackground");
        Services.ThemeManager.StyleTitleBar(this);

        var stack = new StackPanel();

        TitleText = new TextBlock
        {
            Text = summary.Title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextPrimary"),
            Margin = new Thickness(0, 0, 0, 10),
        };
        stack.Children.Add(TitleText);

        foreach (var line in summary.Lines)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = line.Label, Foreground = Brush("TextSecondary") });
            var value = new TextBlock { Text = line.Value, Foreground = Brush("TextPrimary"), TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            LinesPanel.Children.Add(row);
        }

        stack.Children.Add(LinesPanel);

        BareHint = new TextBlock
        {
            Text = T("SummaryNothingElseDesktop"),
            Style = (Style)FindResource("HintText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = summary.IsBare ? Visibility.Visible : Visibility.Collapsed,
        };
        stack.Children.Add(BareHint);

        if (summary.Steps.Count > 0)
        {
            stack.Children.Add(Heading(T("Steps")));
            foreach (var step in summary.Steps)
            {
                StepsPanel.Children.Add(new TextBlock
                {
                    Text = (step.IsDone ? "✓  " : "○  ") + step.Title,
                    Foreground = Brush(step.IsDone ? "TextSecondary" : "TextPrimary"),
                    TextDecorations = step.IsDone ? TextDecorations.Strikethrough : null,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1),
                });
            }

            stack.Children.Add(StepsPanel);
        }

        NotesText = new TextBlock { Text = summary.Notes, Foreground = Brush("TextPrimary"), TextWrapping = TextWrapping.Wrap };
        if (summary.Notes.Length > 0)
        {
            stack.Children.Add(Heading(T("Notes")));
            stack.Children.Add(NotesText);
        }

        var card = new Border
        {
            Style = (Style)FindResource("Card"),
            Padding = new Thickness(18, 14, 18, 16),
            Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 480 },
        };

        // Lapiz (editar) y cerrar, con iconos como el resto de la aplicacion.
        var edit = new Button
        {
            Style = (Style)FindResource("GhostIconButton"),
            Content = "",
            ToolTip = T("EditTaskTooltip"),
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0),
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(edit, "SummaryEdit");
        edit.Click += (_, _) => Edit();

        var close = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = T("Close"), IsCancel = true };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(edit);
        buttons.Children.Add(close);

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(card);
        Content = root;
    }

    /// <summary>El lapiz: se cierra el resumen y quien lo abrio abre el detalle.</summary>
    internal void Edit()
    {
        EditRequested = true;
        Close();
    }

    /// <summary>
    /// Enseña el resumen de una tarea y, si se pulsa el lapiz, abre el detalle con
    /// <paramref name="openEditor"/>.
    /// </summary>
    /// <returns>Lo que devuelva el editor (si cambio algo), o <c>false</c> si no se edito.</returns>
    public static async Task<bool> ShowAsync(Window? owner, TaskService tasks, Guid id, Func<TaskItem, Task<bool>> openEditor)
    {
        var task = await tasks.Repository.GetTaskAsync(id);
        if (task is null || task.Deleted)
        {
            return false;
        }

        var list = await tasks.Repository.GetListAsync(task.ListId);
        var steps = await tasks.Repository.GetStepsAsync(task.Id);
        var window = new TaskSummaryWindow(TaskSummary.Build(task, list?.Name, steps, Localization.Loc.Texts))
        {
            Icon = Services.TrayIconHost.CreateWindowIcon(),
        };

        if (owner is { IsLoaded: true })
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Services.Ventanas.Modal(window);

        return window.EditRequested && await openEditor(task);
    }

    private TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brush("TextPrimary"),
        Margin = new Thickness(0, 12, 0, 4),
    };

    private Brush Brush(string key) => (Brush)FindResource(key);

    private static string T(string key) => Localization.Loc.Get(key);
}
