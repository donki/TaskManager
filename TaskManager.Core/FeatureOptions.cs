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

    /// <summary>
    /// Grupos y gremio (listas compartidas, nivel y rachas). <b>Ocultos</b> (2026-09-01).
    /// </summary>
    /// <remarks>
    /// Se ocultan, no se borran: las tablas, la RLS y las funciones <c>create_group</c> y
    /// <c>join_group</c> siguen enteras en el servidor, y volver a ofrecerlo es poner esto a
    /// <c>true</c>. Se apartan mientras la aplicacion se centra en las tareas de uno.
    /// </remarks>
    public const bool GroupsEnabled = false;
}
