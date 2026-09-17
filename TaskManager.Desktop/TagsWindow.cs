using System.Windows;
using System.Windows.Controls;
using TaskManager.Core.Data;
using TaskManager.Core.Services;

namespace TaskManager.Desktop;

/// <summary>
/// Todas las etiquetas (tambien las que solo llevan tareas hechas), con cuantas tareas tiene cada
/// una y un boton para borrarla de todas. Si la llevan tareas sin acabar, se pregunta.
/// </summary>
public sealed class TagsWindow : Window
{
    private readonly TaskService _tasks;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _empty;

    /// <summary>Se ha borrado alguna: quien abrio la ventana tiene que releer.</summary>
    public bool Changed { get; private set; }

    public TagsWindow(Window owner, TaskService tasks)
    {
        Owner = owner;
        _tasks = tasks;
        Title = T("TagsTitle");
        Width = 440;
        Height = 520;
        MinHeight = 300;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)FindResource("PageBackground");
        Services.ThemeManager.StyleTitleBar(this);

        var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(18, 14, 18, 16) };
        var stack = new DockPanel();
        card.Child = stack;
        var intro = new TextBlock { Text = T("TagsIntro"), Style = (Style)FindResource("HintText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(intro, Dock.Top);
        stack.Children.Add(intro);
        _empty = new TextBlock { Text = T("TagsNone"), Style = (Style)FindResource("HintText"), Visibility = Visibility.Collapsed };
        DockPanel.SetDock(_empty, Dock.Top);
        stack.Children.Add(_empty);
        stack.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var close = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = T("Close"), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => Close();

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(card);
        Content = root;

        Loaded += async (_, _) => await ReloadAsync();
    }

    private static string T(string key) => Localization.Loc.Get(key);

    private async Task ReloadAsync()
    {
        _rows.Children.Clear();
        var tags = await _tasks.Repository.GetTagsAsync(pendingOnly: false);
        _empty.Visibility = tags.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var tag in tags)
        {
            var (pending, total) = await _tasks.Repository.CountTagAsync(tag);
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = "#" + tag, Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"), FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = Localization.Loc.Format("TagsCount", pending, total), Style = (Style)FindResource("HintText"), FontSize = 11 });
            row.Children.Add(text);
            var delete = new Button { Style = (Style)FindResource("DangerIconButton"), Content = "", ToolTip = T("DeleteTag") };
            Grid.SetColumn(delete, 1);
            delete.Click += async (_, _) =>
            {
                var ok = pending > 0
                    ? Controls.ModernDialog.Confirm(this, T("DeleteTag"), Localization.Loc.Format("DeleteTagPending", tag, pending, total), danger: true)
                    : Controls.ModernDialog.Confirm(this, T("DeleteTag"), Localization.Loc.Format("DeleteTagDone", tag, total), danger: true);
                if (!ok)
                    return;
                await _tasks.Repository.DeleteTagAsync(tag);
                Changed = true;
                await ReloadAsync();
            };
            row.Children.Add(delete);
            _rows.Children.Add(row);
        }
    }
}
