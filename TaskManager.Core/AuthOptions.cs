namespace TaskManager.Core;

/// <summary>
/// Como se identifica a quien usa la aplicacion.
/// </summary>
/// <remarks>
/// <para><b>Ahora mismo: sin login.</b> La identidad es el <i>identificador de instalacion</i>, un
/// GUID que se genera la primera vez que se abre la aplicacion y vive en su almacen. No se pide
/// nada al usuario y no hay pantalla de entrada.</para>
///
/// <para><b>Lo que eso implica en el servidor.</b> Toda la RLS esta escrita contra
/// <c>auth.uid()</c>, que sale del JWT que emite Supabase. Sin sesion no hay <c>auth.uid()</c>: las
/// politicas no dejarian leer ni escribir nada. Por eso, cuando se active la sincronizacion, el
/// identificador de instalacion se canjea por una <b>sesion anonima</b> de Supabase
/// (<c>signInAnonymously</c>): el usuario sigue sin ver ninguna pantalla de entrada, pero el
/// servidor recibe un JWT de verdad y la RLS sigue valiendo tal cual esta escrita.</para>
///
/// <para><b>Lo que NO se hace, y por que.</b> Mandar el identificador de instalacion en una
/// cabecera y comparar contra el en las politicas seria tanto como no tener RLS: la clave
/// publicable es publica, y cualquiera podria repetir la peticion con el identificador de otro y
/// leer sus tareas. El identificador sirve para *saber quien eres*, no para *demostrarlo*.</para>
///
/// <para>Para volver a activar la entrada con Google basta poner
/// <see cref="GoogleSignInEnabled"/> a <c>true</c>: el flujo completo (PKCE, navegador del sistema,
/// tokens en el almacen seguro) sigue escrito y probado en <c>SupabaseAuthService</c>.</para>
/// </remarks>
public static class AuthOptions
{
    /// <summary>
    /// Entrada con cuenta. <b>Activada</b> (2026-08-30) porque Windows y Android tienen que
    /// compartir las tareas, y para eso hace falta que las dos sepan que son el mismo usuario.
    /// </summary>
    /// <remarks>
    /// La sesion anonima no sirve para esto: da un usuario distinto en cada dispositivo, asi que
    /// cada uno veria solo lo suyo. Sigue estando para quien no quiera entrar con ninguna cuenta,
    /// pero entonces las tareas se quedan en ese aparato.
    /// </remarks>
    public const bool GoogleSignInEnabled = true;

    /// <summary>
    /// Sesion anonima de Supabase para el identificador de instalacion. Hace falta activar
    /// «Anonymous sign-ins» en el proyecto (Authentication › Sign In / Providers); ahora mismo esta
    /// desactivado, asi que hasta entonces la aplicacion funciona solo en local.
    /// </summary>
    public const bool AnonymousSessionEnabled = true;
}
