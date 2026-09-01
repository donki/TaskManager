using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskManager.Core.Models;
using TaskManager.Core.Services;

namespace TaskManager.Desktop;

/// <summary>
/// Calendario del mes en Windows: la misma vista que en el movil, en una ventana propia.
/// </summary>
/// <remarks>
/// No cabe en el panel de la bandeja, que esta pensado para entrar y salir en dos segundos. Un mes
/// entero necesita sitio, asi que se abre aparte y se cierra cuando se ha visto lo que se queria.
/// </remarks>
public partial class CalendarWindow : Window
{
    private readonly TaskService _tasks;
    private readonly Dictionary<Guid, string> _listNames = [];
    private readonly ObservableCollection<DayTaskRow> _rows = [];

    private DateTime _month = DateTime.Today;
    private DateTime _selected = DateTime.Today;
    private Dictionary<DateTime, List<TaskItem>> _byDay = [];

    public CalendarWindow(TaskService tasks)
    {
        InitializeComponent();

        Services.ThemeManager.StyleTitleBar(this);
        _tasks = tasks;
        DayTasks.ItemsSource = _rows;
    }

    private static CultureInfo Culture =>
        CultureInfo.GetCultureInfo(Localization.Loc.Language);

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await LoadListNamesAsync();
        await ReloadAsync();
    }

    // -----------------------------------------------------------------------

    private async Task LoadListNamesAsync()
    {
        _listNames.Clear();

        foreach (var list in await _tasks.Repository.GetPrivateListsAsync())
        {
            _listNames[list.Id] = list.Name;
        }

        foreach (var group in await _tasks.Repository.GetGroupsAsync())
        {
            foreach (var list in await _tasks.Repository.GetGroupListsAsync(group.Id))
            {
                _listNames[list.Id] = $"{group.Name} · {list.Name}";
            }
        }
    }

    private async Task ReloadAsync()
    {
        var first = new DateTime(_month.Year, _month.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        _byDay = await _tasks.Repository.GetCalendarAsync(first, last);

        MonthLabel.Text = Capitalize(first.ToString("MMMM yyyy", Culture));

        BuildWeekdayHeader();
        BuildMonthGrid(first, last);
        ShowDay(_selected);
    }

    private void BuildWeekdayHeader()
    {
        WeekdayRow.Children.Clear();

        var names = Culture.DateTimeFormat.AbbreviatedDayNames;
        var firstDay = (int)Culture.DateTimeFormat.FirstDayOfWeek;

        for (var i = 0; i < 7; i++)
        {
            WeekdayRow.Children.Add(new TextBlock
            {
                Text = Capitalize(names[(firstDay + i) % 7]),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Brush("TextSecondary"),
            });
        }
    }

    private void BuildMonthGrid(DateTime first, DateTime last)
    {
        MonthGrid.Children.Clear();

        // Casillas en blanco antes del dia 1: la semana no empieza el mismo dia en todos los
        // idiomas, asi que se calcula en vez de darlo por hecho.
        var firstDayOfWeek = (int)Culture.DateTimeFormat.FirstDayOfWeek;
        var offset = ((int)first.DayOfWeek - firstDayOfWeek + 7) % 7;

        for (var i = 0; i < offset; i++)
        {
            MonthGrid.Children.Add(new Border());
        }

        for (var day = 1; day <= last.Day; day++)
        {
            MonthGrid.Children.Add(BuildDayCell(new DateTime(first.Year, first.Month, day)));
        }
    }

    private UIElement BuildDayCell(DateTime date)
    {
        var pending = _byDay.TryGetValue(date, out var list) ? list.Count(t => !t.IsDone) : 0;
        var isToday = date == DateTime.Today;
        var isSelected = date == _selected;

        var number = new TextBlock
        {
            Text = date.Day.ToString(CultureInfo.InvariantCulture),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
            Foreground = isSelected ? Brushes.White : Brush("TextPrimary"),
        };

        // Un punto cuando el dia tiene algo pendiente: se ve sin leer. Poner el numero en cada
        // casilla convierte el mes en una hoja de calculo.
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 5,
            Height = 5,
            Margin = new Thickness(0, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = pending > 0 ? Visibility.Visible : Visibility.Hidden,
            Fill = isSelected ? Brushes.White : Brush("Primary"),
        };

        var cell = new Border
        {
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0, 6, 0, 6),
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(isToday ? 1.5 : 0),
            BorderBrush = Brush("Primary"),
            Background = isSelected ? Brush("Primary") : Brushes.Transparent,
            Child = new StackPanel { Children = { number, dot } },
        };

        cell.MouseLeftButtonUp += (_, _) =>
        {
            _selected = date;
            var first = new DateTime(_month.Year, _month.Month, 1);
            BuildMonthGrid(first, first.AddMonths(1).AddDays(-1));
            ShowDay(date);
        };

        return cell;
    }

    private void ShowDay(DateTime date)
    {
        DayLabel.Text = Capitalize(date.ToString(Localization.Loc.Get("DatePattern"), Culture));

        _rows.Clear();

        foreach (var task in _byDay.TryGetValue(date, out var list) ? list : [])
        {
            _rows.Add(new DayTaskRow(
                task.IsPriority ? "★ " + task.Title : task.Title,
                _listNames.GetValueOrDefault(task.ListId, string.Empty)));
        }

        EmptyLabel.Text = _rows.Count == 0 ? Localization.Loc.Get("CalendarDayEmpty") : string.Empty;
    }

    // -----------------------------------------------------------------------

    private async void OnPreviousMonthClick(object sender, RoutedEventArgs e) => await MoveMonthAsync(-1);

    private async void OnNextMonthClick(object sender, RoutedEventArgs e) => await MoveMonthAsync(1);

    private async Task MoveMonthAsync(int months)
    {
        _month = _month.AddMonths(months);

        // Se marca el dia 1 del mes nuevo: conservar el dia anterior dejaria marcado un 31 que en
        // el mes siguiente puede no existir.
        _selected = new DateTime(_month.Year, _month.Month, 1);

        await ReloadAsync();
    }

    private static Brush Brush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;

    /// <summary>
    /// En español los meses y los dias van en minuscula y quedan mal al empezar una linea. En
    /// ingles ya vienen en mayuscula y esto no cambia nada.
    /// </summary>
    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];

    public sealed record DayTaskRow(string Title, string ListName);
}
