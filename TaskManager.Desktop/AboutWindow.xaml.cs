using System.Windows;
using TaskManager.Core.Services;

namespace TaskManager.Desktop;

/// <summary>
/// El dialogo de «Acerca de»: envuelve <see cref="Views.AboutView"/>.
/// </summary>
/// <remarks>
/// Se abre desde el icono de informacion a la derecha de la franja de marca de la ventana
/// principal, y no como una pestaña mas: «Acerca de» no es un sitio donde se trabaje.
/// </remarks>
public partial class AboutWindow : Window
{
    public AboutWindow(SettingsService settings)
    {
        InitializeComponent();

        Services.ThemeManager.StyleTitleBar(this);

        About.Attach(settings);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
