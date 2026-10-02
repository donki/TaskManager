using TaskManager.Mobile.Pages;
using TaskManager.Mobile.Helpers;

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

        VersionLabel.Text = $"v{Ui.Platform.VersionString}";
    }

    private async void OnMyTasksTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//MyTasksPage");

    private async void OnCalendarTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//CalendarPage");


    private async void OnListsTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//ListsPage");

    private async void OnKanbanTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//KanbanPage");

    private async void OnMailTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//MailPage");

    private async void OnGroupsTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//GroupsPage");

    private async void OnSettingsTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//SettingsPage");

    private async void OnAboutTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//AboutPage");

    private async void OnWhatsNewTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//WhatsNewPage");

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

        switch (Helpers.BackNavigation.Decide(
                    CurrentPage, Navigation.NavigationStack.Count, CurrentItem?.CurrentItem?.CurrentItem?.Route))
        {
            case Helpers.BackAction.Pop:
                return base.OnBackButtonPressed();
            case Helpers.BackAction.GoHome:
                Dispatcher.Dispatch(async () => await Ui.Platform.GoToAsync($"//{Helpers.BackNavigation.HomeRoute}"));
                break;
            case Helpers.BackAction.Hide:
                Ui.Platform.HideApp();
                break;
        }

        return true;
    }

    /// <summary>
    /// Se navega ANTES de cerrar el menu: al reves, la animacion de cierre se come la navegacion y
    /// el menu se cierra sin ir a ninguna parte.
    /// </summary>
    private async Task NavigateAsync(string route)
    {
        await Ui.Platform.GoToAsync(route);
        FlyoutIsPresented = false;
    }
}
