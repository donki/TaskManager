namespace TaskManager.Core;

/// <summary>
/// Que partes de la aplicacion se ofrecen. Un unico sitio donde mirarlo, para que Windows y Android
/// no puedan acabar enseñando cosas distintas.
/// </summary>
public static class FeatureOptions
{
    /// <summary>
    /// Bandeja de correo (Google, Outlook e IMAP con contraseña) para convertir mensajes en tareas.
    /// <b>Oculta</b> (2026-08-31).
    /// </summary>
    /// <remarks>
    /// <para>Se oculta, no se borra: el lector IMAP y el baile de OAuth siguen escritos y probados,
    /// y volver a ofrecerlos es poner esto a <c>true</c>. Borrarlo obligaria a rehacerlo entero.</para>
    ///
    /// <para>Azure DevOps si se quito del todo, porque no era correo ni tarea: era un tablero ajeno
    /// asomandose a esta aplicacion.</para>
    /// </remarks>
    public const bool MailEnabled = false;
}
