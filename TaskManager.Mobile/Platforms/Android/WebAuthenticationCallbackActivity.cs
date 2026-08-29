using Android.App;
using Android.Content;
using Android.Content.PM;

namespace TaskManager.Mobile;

/// <summary>
/// Recoge la vuelta de Google/Supabase. Sin esta actividad y su intent-filter, el navegador se
/// queda con el codigo y la aplicacion nunca se entera de que la entrada ha salido bien.
/// El esquema debe coincidir con <c>MauiOAuthBrowser.RedirectUri</c>.
/// </summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "com.socratic.taskmanager")]
public class WebAuthenticationCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
