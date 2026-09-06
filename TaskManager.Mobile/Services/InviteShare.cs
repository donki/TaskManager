using TaskManager.Core.Services;

namespace TaskManager.Mobile.Services;

/// <summary>
/// Manda la invitacion a un grupo por donde el usuario quiera: <b>el texto y el QR juntos</b>.
/// </summary>
/// <remarks>
/// <para><b>Por que no vale <c>Share.RequestAsync</c> a secas.</b> MAUI tiene dos peticiones y
/// ninguna sirve: <c>ShareTextRequest</c> manda texto sin fichero y <c>ShareFileRequest</c> manda
/// fichero sin texto. Aqui hacen falta los dos —el QR para escanearlo y el enlace escrito para
/// tocarlo— asi que se arma el <c>Intent</c> de Android a mano, que si admite ambos.</para>
///
/// <para>El PNG se escribe en la carpeta de cache y se comparte por el <c>FileProvider</c> que MAUI
/// ya declara: pasar una ruta de fichero pelada en un <c>Intent</c> lo prohibe Android desde la 7
/// (<c>FileUriExposedException</c>).</para>
///
/// <para>Fuera de Android se cae al reparto normal de solo texto: el enlace y el codigo van dentro,
/// asi que la invitacion sigue sirviendo.</para>
/// </remarks>
public static class InviteShare
{
    public static async Task EnviarAsync(string groupName, GroupInvite invite)
    {
        var texts = Helpers.ServiceHelper.GetRequiredService<LocalizationService>();
        var texto = GroupLink.Message(texts, groupName, invite);
        var asunto = texts["GroupInviteSubject"];
        var titulo = texts["ShareTitle"];

#if ANDROID
        try
        {
            var png = GroupLink.QrPng(GroupLink.For(invite));
            var ruta = Path.Combine(FileSystem.CacheDirectory, $"invitacion-{invite.JoinCode}.png");
            await File.WriteAllBytesAsync(ruta, png);

            var contexto = global::Android.App.Application.Context;
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                contexto, $"{contexto.PackageName}.fileProvider", new Java.IO.File(ruta));

            using var intent = new global::Android.Content.Intent(global::Android.Content.Intent.ActionSend);
            intent.SetType("image/png");
            intent.PutExtra(global::Android.Content.Intent.ExtraStream, uri);
            intent.PutExtra(global::Android.Content.Intent.ExtraText, texto);
            intent.PutExtra(global::Android.Content.Intent.ExtraSubject, asunto);
            intent.AddFlags(global::Android.Content.ActivityFlags.GrantReadUriPermission);

            using var elegir = global::Android.Content.Intent.CreateChooser(intent, titulo);
            elegir!.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            contexto.StartActivity(elegir);
            return;
        }
        catch (Exception ex)
        {
            // Si el QR no se puede adjuntar —sin espacio, un FileProvider que no responde— se manda
            // el texto igual: dentro va el enlace, que es lo que de verdad mete a nadie en el grupo.
            System.Diagnostics.Debug.WriteLine($"Compartir QR: {ex.Message}");
        }
#endif

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Text = texto,
            Subject = asunto,
            Title = titulo,
        });
    }
}
