using Android.App;
using Android.Content;
using Android.Content.PM;
using TaskManager.Core.Services;

namespace TaskManager.Mobile;

/// <summary>
/// Recoge la vuelta del navegador. Sin esta actividad y sus intent-filter, la pestaña se queda con
/// el codigo y la aplicacion nunca se entera de que la entrada ha salido bien.
/// </summary>
/// <remarks>
/// <para>Son <b>dos</b> esquemas, y hacen falta los dos:</para>
/// <list type="bullet">
/// <item><c>com.socratic.taskmanager</c>: el propio de la aplicacion. Lo usa Microsoft, que si
/// acepta un esquema cualquiera.</item>
/// <item><c>com.googleusercontent.apps.&lt;id&gt;</c>: el identificador de cliente invertido, que es
/// el unico que admite un cliente de Google de tipo Android. Con el esquema propio responde
/// <c>Error 400: invalid_request</c> (comprobado el 2026-08-30). El valor lo genera
/// <c>oauth.props</c> al compilar, porque el identificador no vive en el repositorio.</item>
/// </list>
/// </remarks>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "com.socratic.taskmanager")]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = OAuthSecrets.GoogleAndroidRedirectScheme)]
public class WebAuthenticationCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
