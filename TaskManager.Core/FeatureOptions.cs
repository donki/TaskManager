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
    /// Grupos: listas compartidas. <b>Activos</b> (2026-09-06).
    /// </summary>
    /// <remarks>
    /// <para>El gremio iba con esta misma llave hasta el 2026-09-09, en que se quito la pantalla:
    /// la cuenta de XP y la racha siguen por dentro —son las que hacen saltar la celebracion— pero
    /// ya no hay nada que enseñar ni que apagar.</para>
    ///
    /// <para>Estuvieron ocultos desde el 2026-09-01 mientras la aplicacion se centraba en las
    /// tareas de uno. Nunca se borraron —las tablas, la RLS y las funciones <c>create_group</c> y
    /// <c>join_group</c> siguieron enteras en el servidor—, asi que volver a ofrecerlos es
    /// exactamente esto.</para>
    ///
    /// <para>Un grupo es de la <b>cuenta</b> que entro en el, como todo lo demas
    /// (<see cref="Models.TaskGroup.AccountId"/>): con dos cuentas en el mismo aparato, cada una ve
    /// los suyos.</para>
    /// </remarks>
    public const bool GroupsEnabled = true;
}
