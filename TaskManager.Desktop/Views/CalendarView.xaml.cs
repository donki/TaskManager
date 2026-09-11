using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskManager.Core.Models;
using TaskManager.Core.Services;

namespace TaskManager.Desktop.Views;

/// <summary>
/// El mes con sus tareas: que dia esta planificada cada cosa y que hay ese dia.
/// </summary>
/// <remarks>
/// <para>Es un control y no una ventana porque se usa en dos sitios a la vez: la pestaña
/// «Calendario» de la ventana principal y la ventana suelta que abre el menu de la bandeja. Antes
/// esto vivia dentro de <see cref="CalendarWindow"/>, y llevarlo a una pestaña habria significado
/// tener el mismo mes escrito dos veces —con dos formas de contar los huecos del dia 1 y dos
/// maneras de marcar hoy— para que acabaran diferenciandose al primer arreglo.</para>
///
/// <para>Un dia se marca con un punto cuando tiene algo pendiente, no con el numero de tareas: el
/// mes se lee de un vistazo, y poner cifras en las 31 casillas lo convierte en una hoja de
/// calculo.</para>
/// </remarks>
public partial class CalendarView : UserControl
{
    private readonly Dictionary<Guid, string> _listNames = [];
    private readonly ObservableCollection<DayTaskRow> _rows = [];

    private TaskService? _tasks;
    private DateTime _month = DateTime.Today;
    private DateTime _selected = DateTime.Today;
    private Dictionary<DateTime, List<TaskItem>> _byDay = [];

    public CalendarView()
    {
        InitializeComponent();
        DayTasks.ItemsSource = _rows;
    }

    /// <summary>
    /// La cultura del idioma elegido, que es la que sabe como se llaman los dias y por cual empieza
    /// la semana. No la del equipo: la aplicacion puede estar en un idioma y Windows en otro.
    /// </summary>
    private static CultureInfo Culture =>
        CultureInfo.GetCultureInfo(Localization.Loc.Language);

    /// <summary>
    /// Le da al calendario de donde leer. El XAML no admite constructores con parametros, asi que
    /// el servicio entra por aqui en cuanto quien monta el control lo tiene.
    /// </summary>
    public async Task AttachAsync(TaskService tasks)
    {
        _tasks = tasks;

        await LoadListNamesAsync();
        await ReloadAsync();
    }

    /// <summary>Vuelve a leer el mes. Se llama cuando algo ha cambiado por fuera.</summary>
    public async Task RefreshAsync()
    {
        if (_tasks is not null)
        {
            await ReloadAsync();
        }
    }

    // -----------------------------------------------------------------------

    private async Task LoadListNamesAsync()
    {
        if (_tasks is null)
        {
            return;
        }

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
        if (_tasks is null)
        {
            return;
        }

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
        var all = _byDay.TryGetValue(date, out var list) ? list : [];
        var pending = all.Count(t => !t.IsDone);
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

        // Un punto cuando el dia tiene algo pendiente: se ve sin leer.
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

            // Cuantas hay ese dia, para el que quiera el detalle sin tener que pulsar.
            ToolTip = all.Count == 0
                ? null
                : Localization.Loc.Format(all.Count == 1 ? "TaskCountOne" : "TaskCount", all.Count),
        };

        cell.MouseLeftButtonUp += (_, _) =>
        {
            _selected = date;
            var first = new DateTime(_month.Year, _month.Month, 1);
            BuildMonthGrid(first, first.AddMonths(1).AddDays(-1));
            ShowDay(date);
        };

        // Doble clic en el dia: a escribir la tarea de ese dia, sin buscar la caja con el raton.
        cell.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                DayAddBox.Focus();
            }
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
                task.Id,
                task.IsPinned ? "📌 " + task.Title : task.Title,
                _listNames.GetValueOrDefault(task.ListId, string.Empty),
                task.IsDone));
        }

        EmptyLabel.Text = _rows.Count == 0 ? Localization.Loc.Get("CalendarDayEmpty") : string.Empty;
    }

    // -----------------------------------------------------------------------

    private async void OnPreviousMonthClick(object sender, RoutedEventArgs e) => await MoveMonthAsync(-1);

    private async void OnDayAddClick(object sender, RoutedEventArgs e) => await AddForDayAsync();

    private async void OnDayAddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await AddForDayAsync();
        }
    }

    /// <summary>
    /// Crear desde el calendario: la tarea nace planificada para el dia elegido, en la primera
    /// lista, y se abre para rematarla, igual que la captura rapida de «Mis tareas».
    /// </summary>
    private async Task AddForDayAsync()
    {
        var title = DayAddBox.Text.Trim();
        if (_tasks is null || title.Length == 0)
        {
            return;
        }

        var list = await _tasks.Repository.GetOrCreateDefaultListAsync(Localization.Loc.Get("DefaultListName"));
        var task = await _tasks.Repository.AddTaskAsync(list.Id, title, plannedFor: _selected);
        DayAddBox.Text = string.Empty;

        await LoadListNamesAsync();
        await ReloadAsync();

        var window = new TaskDetailWindow(_tasks, task)
        {
            Owner = Window.GetWindow(this),
        };

        if (window.ShowDialog() == true && window.Changed)
        {
            await LoadListNamesAsync();
            await ReloadAsync();
        }
    }

    private async void OnNextMonthClick(object sender, RoutedEventArgs e) => await MoveMonthAsync(1);

    private async void OnTodayClick(object sender, RoutedEventArgs e)
    {
        _month = DateTime.Today;
        _selected = DateTime.Today;
        await ReloadAsync();
    }

    /// <summary>Doble clic en una tarea del dia: se abre para tocarla, sin salir del mes.</summary>
    private async void OnDayTaskDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_tasks is null || DayTasks.SelectedItem is not DayTaskRow row)
        {
            return;
        }

        if (await _tasks.Repository.GetTaskAsync(row.Id) is not { } task)
        {
            return;
        }

        var window = new TaskDetailWindow(_tasks, task)
        {
            Owner = Window.GetWindow(this),
        };

        if (window.ShowDialog() == true && window.Changed)
        {
            await LoadListNamesAsync();
            await ReloadAsync();
        }
    }

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

    /// <summary>Una tarea del dia elegido, lista para pintar.</summary>
    public sealed record DayTaskRow(Guid Id, string Title, string ListName, bool IsDone)
    {
        public TextDecorationCollection? Decoration =>
            IsDone ? TextDecorations.Strikethrough : null;

        public double Opacity => IsDone ? 0.55 : 1.0;
    }
}
