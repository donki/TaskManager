using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TaskManager.Core.Services;

namespace TaskManager.Desktop;

/// <summary>
/// Novedades de las cinco ultimas versiones, de la mas nueva a la mas antigua (General 6.7). Sale
/// sola al arrancar por primera vez una version nueva y se abre desde el menu de la bandeja y desde
/// Acerca de. El contenido es el del nucleo (<see cref="WhatsNew"/>), el mismo que en el movil.
/// </summary>
public sealed class WhatsNewWindow : Window
{
    public WhatsNewWindow(SettingsService settings)
    {
        Title = T("MenuWhatsNew");
        Width = 540;
        Height = 620;
        MinWidth = 420;
        MinHeight = 360;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (Brush)FindResource("PageBackground");
        Services.ThemeManager.StyleTitleBar(this);

        var current = CurrentVersion();
        var list = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        var culture = new CultureInfo(Localization.Loc.Language);
        var releases = WhatsNew.Load(Localization.Loc.Language);

        if (releases.Count == 0)
        {
            list.Children.Add(new TextBlock { Text = T("WhatsNewEmpty"), Style = (Style)FindResource("HintText") });
        }

        foreach (var release in releases)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = WhatsNew.SameVersion(release.Version, current)
                    ? Localization.Loc.Format("WhatsNewCurrent", release.Version)
                    : release.Version,
                Style = (Style)FindResource("CardTitle"),
            });

            if (release.Date is { } date)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = date.ToString("D", culture),
                    Style = (Style)FindResource("HintText"),
                    Margin = new Thickness(0, 2, 0, 6),
                });
            }

            foreach (var item in release.Items)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = (Brush)FindResource("Primary"),
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 7, 0, 0),
                });
                var text = new TextBlock
                {
                    Text = item,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    Foreground = (Brush)FindResource("TextPrimary"),
                };
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                stack.Children.Add(row);
            }

            list.Children.Add(new Border
            {
                Style = (Style)FindResource("Card"),
                Padding = new Thickness(20, 16, 20, 18),
                Margin = new Thickness(0, 0, 0, 12),
                Child = stack,
            });
        }

        var close = new Button
        {
            Style = (Style)FindResource("IconButton"),
            Content = "",
            ToolTip = T("Close"),
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        close.Click += (_, _) => Close();

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        Content = root;

        // Vista: hasta la proxima version no vuelve a salir sola.
        _ = settings.MarkVersionSeenAsync(current);
    }

    /// <summary>
    /// La version que esta corriendo, sin lo que .NET añade detras del «+» (el hash del commit).
    /// </summary>
    public static string CurrentVersion()
    {
        var texto = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
        var mas = texto.IndexOf('+');
        return mas > 0 ? texto[..mas] : texto;
    }

    private static string T(string key) => Localization.Loc.Get(key);
}
