using System.Globalization;
using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;
using TaskManager.Mobile.Models;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// Calendario del mes: que dias tienen carga y que hay en el dia elegido.
/// </summary>
/// <remarks>
/// <para>Sirve para lo que "Mi Dia" no puede: ver <b>de un vistazo</b> como esta repartido el
/// trabajo, y darse cuenta de que el jueves hay seis cosas antes de que llegue el jueves.</para>
///
/// <para>La rejilla se construye a mano en vez de usar un control de calendario. Los que trae la
/// plataforma pintan un mes, pero no dejan marcar cada dia con su carga —que es justo lo unico que
/// aporta esta pantalla— y ademas empiezan la semana donde diga el sistema operativo, no donde
/// diga el idioma elegido en la aplicacion.</para>
/// </remarks>
public partial class CalendarPage : ContentPage
{
    private readonly TaskService _tasks;
    private readonly Dictionary<Guid, string> _listNames = [];

    private DateTime _month = DateTime.Today;
    private DateTime _selected = DateTime.Today;
    private Dictionary<DateTime, List<TaskItem>> _byDay = [];

    public CalendarPage()
        : this(ServiceHelper.GetRequiredService<TaskService>())
    {
    }

    public CalendarPage(TaskService tasks)
    {
        InitializeComponent();
        _tasks = tasks;
    }

    private CultureInfo Culture => CultureInfo.GetCultureInfo(Localization.Loc.Instance.Language);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _tasks.InitializeAsync();
        await LoadListNamesAsync();
        await ReloadAsync();
    }

    // -----------------------------------------------------------------------
    // Carga
    // -----------------------------------------------------------------------

    /// <summary>
    /// Refrescar: habla con el servidor y vuelve a pintar. No es solo repintar lo de aqui — lo que
    /// se quiere saber al pulsarlo es si hay algo nuevo del otro dispositivo.
    /// </summary>
    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await Helpers.ServiceHelper.GetRequiredService<SyncCoordinator>().RefreshNowAsync();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var first = new DateTime(_month.Year, _month.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        // Se pide el mes completo de una vez y luego se reparte: preguntar dia a dia serian
        // treinta y una consultas para pintar una sola pantalla.
        _byDay = await _tasks.Repository.GetCalendarAsync(first, last);

        MonthLabel.Text = Capitalize(first.ToString("MMMM yyyy", Culture));

        BuildWeekdayHeader();
        BuildMonthGrid(first, last);
        ShowDay(_selected);
    }

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

    // -----------------------------------------------------------------------
    // Rejilla
    // -----------------------------------------------------------------------

    private void BuildWeekdayHeader()
    {
        WeekdayRow.Children.Clear();

        var names = Culture.DateTimeFormat.AbbreviatedDayNames;
        var firstDay = (int)Culture.DateTimeFormat.FirstDayOfWeek;

        for (var i = 0; i < 7; i++)
        {
            var label = new Label
            {
                Text = Capitalize(names[(firstDay + i) % 7]),
                Style = Resource<Style>("HintText"),
                HorizontalTextAlignment = TextAlignment.Center,
            };

            Grid.SetColumn(label, i);
            WeekdayRow.Children.Add(label);
        }
    }

    private void BuildMonthGrid(DateTime first, DateTime last)
    {
        MonthGrid.Children.Clear();

        // Cuantas casillas quedan en blanco antes del dia 1: la semana no empieza en lunes en
        // todos los idiomas, asi que se calcula, no se da por hecho.
        var firstDayOfWeek = (int)Culture.DateTimeFormat.FirstDayOfWeek;
        var offset = ((int)first.DayOfWeek - firstDayOfWeek + 7) % 7;

        for (var day = 1; day <= last.Day; day++)
        {
            var date = new DateTime(first.Year, first.Month, day);
            var index = offset + day - 1;

            var view = BuildDayCell(date);
            Grid.SetColumn(view, index % 7);
            Grid.SetRow(view, index / 7);
            MonthGrid.Children.Add(view);
        }
    }

    private View BuildDayCell(DateTime date)
    {
        var count = _byDay.TryGetValue(date, out var list) ? list.Count(t => !t.IsDone) : 0;
        var isToday = date == DateTime.Today;
        var isSelected = date == _selected;

        var number = new Label
        {
            Text = date.Day.ToString(CultureInfo.InvariantCulture),
            FontSize = 14,
            HorizontalTextAlignment = TextAlignment.Center,
            FontAttributes = isToday ? FontAttributes.Bold : FontAttributes.None,
        };

        // Un punto por debajo del numero cuando el dia tiene algo pendiente. Un punto se ve sin
        // leer; poner el numero de tareas en cada casilla convierte el mes en una hoja de calculo.
        var dot = new BoxView
        {
            HeightRequest = 5,
            WidthRequest = 5,
            CornerRadius = 3,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 2, 0, 0),
            IsVisible = count > 0,
            Color = Resource<Color>("Primary"),
        };

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(0, 6),
            Children = { number, dot },
        };

        var border = new Border
        {
            Content = content,
            StrokeThickness = isToday ? 1.5 : 0,
            Stroke = Resource<Color>("Primary"),
            BackgroundColor = isSelected ? Resource<Color>("Primary") : Colors.Transparent,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
        };

        if (isSelected)
        {
            number.TextColor = Colors.White;
            dot.Color = Colors.White;
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _selected = date;
            BuildMonthGrid(new DateTime(_month.Year, _month.Month, 1),
                new DateTime(_month.Year, _month.Month, 1).AddMonths(1).AddDays(-1));
            ShowDay(date);
        };

        border.GestureRecognizers.Add(tap);
        return border;
    }

    // -----------------------------------------------------------------------
    // Dia elegido
    // -----------------------------------------------------------------------

    private void ShowDay(DateTime date)
    {
        DayLabel.Text = Capitalize(date.ToString(Localization.Loc.Instance["DatePattern"], Culture));
        EmptyLabel.Text = Localization.Loc.Instance["CalendarDayEmpty"];

        var tasks = _byDay.TryGetValue(date, out var list) ? list : [];

        DayTasksView.ItemsSource = tasks
            .Select(t => new TaskRow(t, _listNames.GetValueOrDefault(t.ListId, string.Empty)))
            .ToList();
    }

    private async void OnTaskTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TaskRow row)
        {
            await Shell.Current.GoToAsync($"{nameof(TaskDetailPage)}?taskId={row.Id}");
        }
    }

    // -----------------------------------------------------------------------

    private async void OnPreviousMonthClicked(object? sender, EventArgs e) => await MoveMonthAsync(-1);

    private async void OnNextMonthClicked(object? sender, EventArgs e) => await MoveMonthAsync(1);

    private async Task MoveMonthAsync(int months)
    {
        _month = _month.AddMonths(months);

        // Al cambiar de mes se elige el dia 1, no se conserva el que estuviera marcado: si no,
        // saltar de un mes de 31 dias a uno de 30 dejaria marcado un dia que no existe.
        _selected = new DateTime(_month.Year, _month.Month, 1);

        await ReloadAsync();
    }

    /// <summary>
    /// Busca un recurso del diccionario de la aplicacion. Se resuelve asi, y no con
    /// <c>Resources[...]</c> de la pagina, porque los estilos viven en el diccionario global y
    /// desde la pagina solo se ven los suyos.
    /// </summary>
    private static T Resource<T>(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is T typed
            ? typed
            : default!;

    /// <summary>
    /// En español los meses y los dias van en minuscula, y al empezar una linea quedan mal. En
    /// ingles ya vienen en mayuscula y esto no cambia nada.
    /// </summary>
    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
}
