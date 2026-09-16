namespace TaskManager.Mobile.Services;

/// <summary>
/// La imagen que haya en el portapapeles, si hay alguna. MAUI (<c>Clipboard.Default</c>) solo sabe
/// de texto; en Android las imagenes copiadas van como un <c>content://</c> en el ClipData, y se
/// leen con el ContentResolver.
/// </summary>
public static class ClipboardImage
{
    /// <summary>Bytes y extension (.png, .jpg…) de la imagen, o null si no hay imagen.</summary>
    public static Task<(byte[] Bytes, string Extension)?> ReadAsync()
    {
#if ANDROID
        return Task.Run(() =>
        {
            var context = Android.App.Application.Context;
            if (context.GetSystemService(Android.Content.Context.ClipboardService) is not Android.Content.ClipboardManager clipboard)
            {
                return ((byte[], string)?)null;
            }
            var clip = clipboard.PrimaryClip;
            if (clip is null || clip.ItemCount == 0)
            {
                return null;
            }

            for (var i = 0; i < clip.ItemCount; i++)
            {
                var uri = clip.GetItemAt(i)?.Uri;
                if (uri is null)
                {
                    continue;
                }
                var mime = context.ContentResolver?.GetType(uri) ?? string.Empty;
                if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                using var stream = context.ContentResolver?.OpenInputStream(uri);
                if (stream is null)
                {
                    continue;
                }
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var extension = mime.ToLowerInvariant() switch
                {
                    "image/jpeg" => ".jpg",
                    "image/gif" => ".gif",
                    "image/webp" => ".webp",
                    _ => ".png",
                };
                return (memory.ToArray(), extension);
            }
            return null;
        });
#else
        return Task.FromResult<(byte[], string)?>(null);
#endif
    }
}
