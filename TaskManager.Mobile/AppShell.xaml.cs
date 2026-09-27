using TaskManager.Mobile.Pages;

namespace TaskManager.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Pagina de detalle: no esta en el menu, se llega desde una lista o desde un grupo.
        Routing.RegisterRoute(nameof(ListDetailPage), typeof(ListDetailPage));
        Routing.RegisterRoute(nameof(TaskDetailPage), typeof(TaskDetailPage));

        // El correo esta oculto: la fila del menu no se enseña y la ruta no se ofrece.
        MailMenuRow.IsVisible = TaskManager.Core.FeatureOptions.MailEnabled;

        // Grupos: oculto mientras FeatureOptions.GroupsEnabled sea false.
        GroupsMenuRow.IsVisible = TaskManager.Core.FeatureOptions.GroupsEnabled;

        VersionLabel.Text = $"v{AppInfo.Current.VersionString}";
    }

    private async void OnMyTasksTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//MyTasksPage");

    private async void OnCalendarTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//CalendarPage");


    private async void OnListsTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//ListsPage");

    private async void OnKanbanTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//KanbanPage");

    private async void OnMailTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//MailPage");

    private async void OnGroupsTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//GroupsPage");

    private async void OnSettingsTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//SettingsPage");

    private async void OnAboutTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//AboutPage");

    /// <summary>
    /// Boton de atras del movil (Mobile 7). Primero se cierra lo que haya encima: el menu lateral,
    /// un dialogo, o lo que la propia pantalla tenga abierto (seleccion, buscador, cambios sin
    /// guardar). Despues, con una pantalla apilada (detalle, lista, etiquetas, QR) se vuelve a la
    /// anterior; desde otra pantalla del menu se vuelve a «Mis tareas», y en «Mis tareas» (o en la
    /// entrada, si no hay sesion) la aplicacion se oculta.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        if (FlyoutIsPresented)
        {
            FlyoutIsPresented = false;
            return true;
        }

        var page = CurrentPage;
        if (Helpers.BackNavigation.CloseDialog(page))
        {
            return true;
        }

        if (page is Helpers.IBackHandler handler && handler.HandleBack())
        {
            return true;
        }

        if (Navigation.NavigationStack.Count > 1)
        {
            return base.OnBackButtonPressed();
        }

        var route = CurrentItem?.CurrentItem?.CurrentItem?.Route;
        if (route is not (HomeRoute or "LoginPage"))
        {
            Dispatcher.Dispatch(async () => await GoToAsync($"//{HomeRoute}"));
            return true;
        }

        Helpers.BackNavigation.HideApp();
        return true;
    }

    private const string HomeRoute = "MyTasksPage";

    /// <summary>
    /// Se navega ANTES de cerrar el menu: al reves, la animacion de cierre se come la navegacion y
    /// el menu se cierra sin ir a ninguna parte.
    /// </summary>
    private async Task NavigateAsync(string route)
    {
        await GoToAsync(route);
        FlyoutIsPresented = false;
    }
}
