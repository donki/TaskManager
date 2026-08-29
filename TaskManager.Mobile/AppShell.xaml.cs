using TaskManager.Mobile.Pages;

namespace TaskManager.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Pagina de detalle: no esta en el menu, se llega desde una lista o desde un grupo.
        Routing.RegisterRoute(nameof(ListDetailPage), typeof(ListDetailPage));

        VersionLabel.Text = $"v{AppInfo.Current.VersionString}";
    }

    private async void OnMyDayTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//MyDayPage");

    private async void OnListsTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//ListsPage");

    private async void OnGroupsTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//GroupsPage");

    private async void OnBoardTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//BoardPage");

    private async void OnSettingsTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//SettingsPage");

    private async void OnAboutTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//AboutPage");

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
