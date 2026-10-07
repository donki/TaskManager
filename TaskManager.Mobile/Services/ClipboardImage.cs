namespace TaskManager.Mobile.Services;

/// <summary>
/// La imagen que haya en el portapapeles, si hay alguna. MAUI (<c>Clipboard.Default</c>) solo sabe
/// de texto; en Android las imagenes copiadas van como un <c>content://</c> en el ClipData, y se
/// leen con el ContentResolver.
/// </summary>
/// <remarks>
/// El ClipData se pide en el hilo principal: desde Android 10 solo puede leer el portapapeles la
/// aplicacion que tiene el foco, y antes se pedia desde otro hilo. Los bytes, en cambio, se leen
/// aparte para no trabar la pantalla. Si el proveedor no dice el tipo, decide
/// <see cref="PastedImage"/> mirando los bytes; y si lo copiado no trae <c>content://</c> sino HTML
/// con la imagen dentro (<c>data:image/…</c>), se saca de ahi.
/// </remarks>
public static class ClipboardImage
{
    /// <summary>Bytes y extension (.png, .jpg…) de la imagen, o null si no hay imagen.</summary>
    public static async Task<(byte[] Bytes, string Extension)?> ReadAsync()
    {
#if ANDROID
        var context = Android.App.Application.Context;
        var clip = await MainThread.InvokeOnMainThreadAsync(() =>
            context.GetSystemService(Android.Content.Context.ClipboardService) is Android.Content.ClipboardManager clipboard
                ? clipboard.PrimaryClip
                : null);
        if (clip is null || clip.ItemCount == 0)
        {
            return null;
        }

        var hint = PastedImage.ImageMime(clip.Description);
        var items = Enumerable.Range(0, clip.ItemCount)
            .Select(clip.GetItemAt)
            .OfType<Android.Content.ClipData.Item>()
            .ToList();

        return await Task.Run(() =>
        {
            foreach (var item in items)
            {
                if (PastedImage.Read(context, item, hint) is { } image)
                {
                    return image;
                }
            }

            return ((byte[], string)?)null;
        });
#else
        await Task.CompletedTask;
        return null;
#endif
    }
}
