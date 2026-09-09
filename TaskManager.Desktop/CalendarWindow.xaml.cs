using System.Windows;
using TaskManager.Core.Services;

namespace TaskManager.Desktop;

/// <summary>
/// Calendario del mes en una ventana propia, para el menu de la bandeja.
/// </summary>
/// <remarks>
/// Lo que se ve dentro es <see cref="Views.CalendarView"/>, el mismo control que la pestaña
/// «Calendario» de la ventana principal: el mes se escribe una vez y se enseña en los dos sitios.
/// </remarks>
public partial class CalendarWindow : Window
{
    private readonly TaskService _tasks;

    public CalendarWindow(TaskService tasks)
    {
        InitializeComponent();

        Services.ThemeManager.StyleTitleBar(this);
        _tasks = tasks;
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await View.AttachAsync(_tasks);
    }
}
