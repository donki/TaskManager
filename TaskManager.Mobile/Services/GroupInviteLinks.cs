using TaskManager.Core.Services;

namespace TaskManager.Mobile.Services;

/// <summary>
/// La invitacion que llega de fuera —de un QR escaneado o de un enlace tocado— esperando a que la
/// aplicacion este lista para usarla.
/// </summary>
/// <remarks>
/// <para><b>Por que hace falta guardarla.</b> El enlace puede llegar con la aplicacion cerrada: el
/// sistema la arranca, la actividad recibe el <c>Intent</c> y en ese momento no hay ni Shell, ni
/// sesion, ni pantalla a la que llevar a nadie. Se apunta aqui y la recoge la pantalla de grupos
/// cuando de verdad se puede hacer algo con ella.</para>
///
/// <para>Se consume <b>una sola vez</b> (<see cref="Recoger"/>): si no, volver a la pantalla de
/// grupos intentaria entrar en el mismo grupo una y otra vez.</para>
/// </remarks>
public static class GroupInviteLinks
{
    private static GroupInvite? _pendiente;

    /// <summary>Hay una invitacion esperando.</summary>
    public static bool Hay => _pendiente is not null;

    /// <summary>Apunta lo que traiga el enlace. Lo que no sea una invitacion se ignora.</summary>
    public static void Anotar(Uri? link)
    {
        if (GroupLink.Read(link) is { } invite)
        {
            _pendiente = invite;
        }
    }

    /// <summary>Se la lleva quien vaya a usarla, y deja de estar pendiente.</summary>
    public static GroupInvite? Recoger()
    {
        var invite = _pendiente;
        _pendiente = null;
        return invite;
    }
}
