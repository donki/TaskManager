using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
using TaskManager.Core.Services;
using AndroidView = Android.Views.View;

namespace TaskManager.Mobile;

/// <remarks>
/// El <c>intent-filter</c> de <c>taskmanager://join</c> es lo que hace que el QR de una invitacion
/// sirva de algo: la camara del telefono ve el enlace, el sistema ve que esta aplicacion lo entiende
/// y la abre con el codigo y la clave dentro (ver <see cref="GroupLink"/>).
/// </remarks>
[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    Exported = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = GroupLink.Scheme,
    DataHost = "join")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ApplySystemBarInsets();
        Anotar(Intent);
    }

    /// <summary>
    /// La aplicacion ya estaba abierta y llega otro enlace. Sin esto, escanear un QR con la
    /// aplicacion en segundo plano la traeria al frente sin enterarse de la invitacion, porque
    /// <c>OnCreate</c> no se vuelve a ejecutar (la actividad es <c>SingleTop</c>).
    /// </summary>
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Anotar(intent);
    }

    private static void Anotar(Intent? intent)
    {
        if (intent?.Data?.ToString() is { Length: > 0 } data && Uri.TryCreate(data, UriKind.Absolute, out var link))
        {
            Services.GroupInviteLinks.Anotar(link);
        }
    }

    /// <summary>
    /// Desde Android 15 el sistema dibuja de borde a borde: se separa el contenido del reloj y de
    /// la barra inferior, y se pinta el hueco con el indigo de marca (constitucion E.3).
    /// </summary>
    private void ApplySystemBarInsets()
    {
        var content = FindViewById(global::Android.Resource.Id.Content);
        if (content is null)
            return;

        content.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#2A1CB8"));
        ViewCompat.SetOnApplyWindowInsetsListener(content, new SystemBarInsetsListener());

        var controller = Window is not null ? WindowCompat.GetInsetsController(Window, Window.DecorView) : null;
        if (controller is not null)
        {
            controller.AppearanceLightStatusBars = false;
            controller.AppearanceLightNavigationBars = false;
        }
    }

    private sealed class SystemBarInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(AndroidView? view, WindowInsetsCompat? insets)
        {
            var consumed = WindowInsetsCompat.Consumed!;
            if (view is null || insets is null)
                return consumed;

            var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout());
            if (bars is not null)
                view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);

            return consumed;
        }
    }
}
