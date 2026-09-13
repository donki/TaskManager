namespace TaskManager.Core;

/// <summary>
/// Como se identifica a quien usa la aplicacion.
/// </summary>
/// <remarks>
/// <para><b>Con cuenta, y es obligatorio</b> (2026-08-31). Se puede entrar con <b>Google</b> o con
/// <b>Microsoft</b>. La identidad es el identificador de la cuenta —el <c>sub</c> de Google o el
/// <c>oid</c> de Microsoft—: el mismo en Windows y en Android, y no cambia aunque el usuario se
/// cambie el nombre o el correo. El nombre de la cuenta es tambien el nombre en la aplicacion.</para>
///
/// <para><b>Por que ya no se puede seguir sin cuenta.</b> El identificador de instalacion era un
/// GUID por aparato: daba un usuario distinto en cada equipo, asi que Windows y Android no podian
/// compartir nada. Lo mismo pasa con la sesion anonima de Supabase, que ademas gasta una fila de
/// <c>auth.users</c> por instalacion. Compartir las tareas exige que los dos lados sepan que son la
/// misma persona, y eso solo lo puede decir una cuenta.</para>
///
/// <para><b>El proveedor se habla de frente, no a traves de Supabase.</b> Ver
/// <see cref="Services.IdentitySignInService"/>: pasando por <c>/auth/v1/authorize</c> la entrada
/// dependia de que el proyecto tuviera el proveedor dado de alta, y sin eso el navegador se plantaba
/// en una pagina de error de Supabase. La sesion del proyecto se consigue despues, canjeando el
/// id_token, y es lo unico que hace falta para sincronizar.</para>
/// </remarks>
public static class AuthOptions
{
    /// <summary>Entrada con cuenta. Los proveedores concretos los decide IdentitySignInService.</summary>
    public const bool GoogleSignInEnabled = true;

    /// <summary>
    /// Entrada con Microsoft, ademas de con Google.
    /// </summary>
    /// <remarks>
    /// <para>PKCE contra Entra, <c>oid</c> como identidad —y no <c>sub</c>, que es distinto por
    /// aplicacion— y canje del id_token por la sesion del proyecto como proveedor «azure», que es
    /// como Supabase sigue llamando a Entra ID.</para>
    ///
    /// <para><b>Activo desde el 2026-09-03.</b> Estuvo escrito y oculto mientras no se hubiera
    /// entrado de verdad con el. Lo que faltaba no era el flujo sino lo de debajo: en el mismo
    /// aparato conviven las dos cuentas y <b>cada una tiene sus listas</b>
    /// (<see cref="Models.TaskList.AccountId"/>). Sin esa separacion, cambiar de cuenta enseñaba
    /// las tareas de la anterior mezcladas con las que bajaban del servidor de la nueva.</para>
    /// </remarks>
    public const bool MicrosoftSignInEnabled = true;

    /// <summary>
    /// No se puede usar la aplicacion sin entrar: la pantalla de entrada no tiene salida y el
    /// arranque no continua hasta que hay cuenta. «Seguir sin cuenta» tambien es entrar (ver
    /// <see cref="LocalModeEnabled"/>): la puerta sigue siendo la misma.
    /// </summary>
    public const bool SignInRequired = true;

    /// <summary>
    /// «Seguir sin cuenta»: se entra con un identificador propio de la instalacion
    /// (<see cref="Services.IdentityProvider.Local"/>) y todo se queda en el aparato.
    /// </summary>
    /// <remarks>
    /// <para><b>Pedido el 2026-09-13.</b> No contradice lo de arriba: lo que obligaba a entrar era
    /// que dos aparatos pudieran reconocerse como la misma persona, y quien elige el modo local
    /// esta renunciando a eso a sabiendas. Se le dice claro: ni sincroniza ni comparte grupos, y si
    /// desinstala, se pierde.</para>
    ///
    /// <para><b>Es una cuenta mas</b> a efectos de listas (<see cref="Models.TaskList.AccountId"/>):
    /// entrar luego con Google no se lleva nada, y volver a «sin cuenta» encuentra lo suyo, porque
    /// el identificador local se guarda y no cambia.</para>
    /// </remarks>
    public const bool LocalModeEnabled = true;
}
