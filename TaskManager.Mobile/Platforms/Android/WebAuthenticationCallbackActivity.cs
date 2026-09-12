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
/// <para>Son <b>tres</b> esquemas, y hacen falta los tres:</para>
/// <list type="bullet">
/// <item><c>com.socratic.taskmanager</c>: el propio de la aplicacion. Lo usa Microsoft, que si
/// acepta un esquema cualquiera.</item>
/// <item><c>com.googleusercontent.apps.&lt;id de escritorio&gt;</c>: el identificador invertido del
/// cliente de <i>escritorio</i> de Google. Es por donde vuelve la <b>entrada</b> con Google desde
/// el 2026-09-12: ese cliente no valida paquete ni firma, y volver por intent no necesita red
/// —el servidor local de antes se quedaba sin paquetes en cuanto el navegador tapaba la
/// aplicacion (ver <c>IdentitySignInService</c>).</item>
/// <item><c>com.googleusercontent.apps.&lt;id de Android&gt;</c>: el del cliente de tipo Android,
/// que es el unico que admite ese cliente. Lo usa el correo. Con el esquema propio responde
/// <c>Error 400: invalid_request</c> (comprobado el 2026-08-30).</item>
/// </list>
/// <para>Los dos valores de Google los genera <c>oauth.props</c> al compilar, porque los
/// identificadores no viven en el repositorio.</para>
/// </remarks>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "com.socratic.taskmanager")]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = OAuthSecrets.GoogleDesktopRedirectScheme)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = OAuthSecrets.GoogleAndroidRedirectScheme)]
public class WebAuthenticationCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
