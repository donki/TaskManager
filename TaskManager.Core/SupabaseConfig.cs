namespace TaskManager.Core;

/// <summary>
/// Datos del proyecto de Supabase, incrustados en la aplicacion.
/// </summary>
/// <remarks>
/// <para>Es **una sola base de datos para todos los grupos**: no hay un proyecto por grupo ni nada
/// que configurar. La aplicacion no pide credenciales a nadie — quien entra lo hace con su cuenta
/// de Google y la separacion entre grupos la resuelve la RLS del servidor.</para>
///
/// <para><b>Por que la clave puede ir aqui.</b> La clave publicable esta pensada para viajar en el
/// cliente: es el equivalente de la antigua <c>anon key</c> y por si sola no da acceso a ningun
/// dato. Lo que protege las filas es la RLS (y, en el contenido de los grupos, el cifrado con la
/// clave compartida). La clave **secreta** (<c>sb_secret_...</c>) se salta la RLS entera y por eso
/// no aparece en el codigo ni puede aparecer nunca: si hiciera falta, iria en una Edge Function.</para>
/// </remarks>
public static class SupabaseConfig
{
    /// <summary>URL del proyecto. Sin <c>/rest/v1</c> ni <c>/auth/v1</c>: eso lo pone cada cliente.</summary>
    public const string Url = "https://zugcayvpnsespbwjluyz.supabase.co";

    /// <summary>Clave publicable (equivalente a la anon key). Publica por diseño.</summary>
    public const string PublishableKey = "sb_publishable_YyPcZFTG5sF5yF7v6cU0wA_Hm95WBhz";

    public static bool IsConfigured => Url.Length > 0 && PublishableKey.Length > 0;
}
