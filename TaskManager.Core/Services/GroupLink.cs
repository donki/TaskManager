using QRCoder;

namespace TaskManager.Core.Services;

/// <summary>
/// La invitacion a un grupo, como <b>enlace</b> y como <b>codigo QR</b>.
/// </summary>
/// <remarks>
/// <para><b>Para que.</b> Dictar un codigo de seis letras y una clave de treinta y seis caracteres
/// por telefono no lo hace nadie. Con el enlace basta con tocarlo, y con el QR basta con enfocarlo
/// con la camara: la aplicacion se abre con el grupo y la clave ya puestos.</para>
///
/// <para><b>Por que un esquema propio y no una direccion web.</b> Un <c>https://</c> obligaria a
/// tener una pagina que redirigiera, o sea un servidor mas que mantener y una direccion por la que
/// pasa la clave de un grupo. <c>taskmanager://join</c> lo resuelve el propio aparato: si la
/// aplicacion esta instalada, se abre; si no, no pasa nada y ahi sigue el codigo escrito debajo
/// para meterlo a mano.</para>
///
/// <para><b>La clave viaja en el enlace</b>, y por eso el enlace es tan secreto como ella: quien lo
/// tenga entra en el grupo. Es lo mismo que ocurre con cualquier invitacion de este tipo, y por eso
/// se puede rehacer cuando haga falta (<see cref="ISyncService.RenewInviteAsync"/>), que deja la
/// anterior sin valor.</para>
/// </remarks>
public static class GroupLink
{
    /// <summary>El esquema que abre la aplicacion. Va tambien en el manifiesto de Android.</summary>
    public const string Scheme = "taskmanager";

    /// <summary>Lo que se escribe en el QR y en el enlace que se comparte.</summary>
    public static Uri For(GroupInvite invite) =>
        new($"{Scheme}://join?code={Uri.EscapeDataString(invite.JoinCode)}" +
            $"&key={Uri.EscapeDataString(invite.SharedKey)}");

    /// <summary>
    /// El texto de la invitacion, tal y como se manda por correo o por WhatsApp: el nombre del
    /// grupo, el enlace que lo abre todo, y el codigo y la clave sueltos por si hay que teclearlos.
    /// </summary>
    /// <remarks>
    /// Se arma aqui, en el nucleo, para que Windows y Android manden <b>exactamente</b> el mismo
    /// mensaje: tener el texto en cada interfaz acaba con dos invitaciones distintas segun quien la
    /// mande.
    /// </remarks>
    public static string Message(LocalizationService texts, string groupName, GroupInvite invite) =>
        texts.Format("GroupInviteBody", groupName, invite.JoinCode, invite.SharedKey, For(invite));

    /// <summary>
    /// Lee una invitacion de un enlace. Devuelve <c>null</c> si no es de los nuestros o le falta
    /// algo: un enlace a medias no puede acabar en un intento de entrada con la clave vacia.
    /// </summary>
    public static GroupInvite? Read(Uri? link)
    {
        if (link is null || !string.Equals(link.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var code = Parametro(link, "code");
        var key = Parametro(link, "key");

        return code.Length > 0 && key.Length > 0 ? new GroupInvite(code, key) : null;
    }

    /// <summary>
    /// El QR del enlace, en PNG.
    /// </summary>
    /// <remarks>
    /// <para>Se usa <see cref="PngByteQRCode"/> y no el que dibuja con <c>System.Drawing</c>: ese no
    /// existe en Android. Asi el mismo codigo vale en las dos aplicaciones y devuelve unos bytes que
    /// cada interfaz pinta como sepa.</para>
    ///
    /// <para>Correccion de errores media (<c>Q</c>, un 25%): el codigo sigue leyendose con la
    /// pantalla sucia o con reflejos, que es como se escanea de verdad, y todavia cabe holgado.</para>
    /// </remarks>
    public static byte[] QrPng(Uri link, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(link.ToString(), QRCodeGenerator.ECCLevel.Q);

        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }

    private static string Parametro(Uri link, string nombre)
    {
        foreach (var pareja in link.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var partes = pareja.Split('=', 2);
            if (partes.Length == 2 && string.Equals(partes[0], nombre, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(partes[1]);
            }
        }

        return string.Empty;
    }
}
