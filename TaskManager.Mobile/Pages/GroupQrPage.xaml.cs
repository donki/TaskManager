using TaskManager.Core.Services;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// La invitacion a un grupo, para enseñarsela a alguien: el <b>QR</b> grande, el codigo y la clave
/// escritos debajo, y los botones para mandarla o copiarla.
/// </summary>
/// <remarks>
/// <para>El QR lleva dentro <c>taskmanager://join?code=…&amp;key=…</c>: quien lo enfoque con la
/// camara abre esta misma aplicacion ya con el grupo puesto, sin teclear un codigo de seis letras
/// ni una clave de treinta y seis caracteres (ver <see cref="GroupLink"/>).</para>
///
/// <para>Es una pagina y no un aviso porque hay que <b>enseñarla</b>: se deja abierta mientras el
/// otro apunta con su telefono.</para>
/// </remarks>
public partial class GroupQrPage : ContentPage
{
    private readonly string _groupName;
    private readonly GroupInvite _invite;

    public GroupQrPage(string groupName, GroupInvite invite)
    {
        InitializeComponent();

        _groupName = groupName;
        _invite = invite;

        var png = GroupLink.QrPng(GroupLink.For(invite));
        QrImage.Source = ImageSource.FromStream(() => new MemoryStream(png));

        HintLabel.Text = Localization.Loc.Instance["QrHint"];
        CodeLabel.Text = Localization.Loc.Instance.Format("GroupCodeOnly", invite.JoinCode);
        KeyLabel.Text = invite.SharedKey;
    }

    private string Texto() => GroupLink.Message(
        Helpers.ServiceHelper.GetRequiredService<LocalizationService>(), _groupName, _invite);

    private async void OnShareClicked(object? sender, EventArgs e) =>
        await Services.InviteShare.EnviarAsync(_groupName, _invite);

    private async void OnCloseClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync();

    private async void OnCopyClicked(object? sender, EventArgs e)
    {
        await Clipboard.SetTextAsync(Texto());
        await SocShared.ModernDialog.AlertAsync(
            this,
            Localization.Loc.Instance["InviteTitle"],
            Localization.Loc.Instance["GroupInviteSaved"],
            Localization.Loc.Instance["Ok"]);
    }
}
