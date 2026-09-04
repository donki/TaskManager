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
    /// arranque no continua hasta que hay cuenta.
    /// </summary>
    public const bool SignInRequired = true;
}
