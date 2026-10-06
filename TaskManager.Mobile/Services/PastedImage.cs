namespace TaskManager.Mobile.Services;

/// <summary>
/// Si unos bytes pegados son una imagen y con que extension guardarla.
/// </summary>
/// <remarks>
/// <para>El tipo que da Android no siempre esta: segun quien haya copiado la imagen,
/// <c>ContentResolver.GetType</c> devuelve <c>null</c> o <c>application/octet-stream</c>, y la
/// imagen se descartaba por no parecerlo. Por eso, si el tipo no dice nada, se miran los primeros
/// bytes, que en los formatos de imagen son fijos.</para>
///
/// <para>Vive fuera de los <c>#if ANDROID</c> para poder probarlo sin dispositivo.</para>
/// </remarks>
public static class PastedImage
{
    /// <summary>
    /// La extension (<c>.png</c>, <c>.jpg</c>…) si es una imagen; <c>null</c> si no lo es.
    /// </summary>
    public static string? Extension(string? mime, ReadOnlySpan<byte> bytes)
    {
        var fromMime = (mime ?? string.Empty).ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/heic" or "image/heif" => ".heic",
            _ => null,
        };

        if (fromMime is not null)
        {
            return fromMime;
        }

        // El tipo no lo dice (o es uno de imagen que no conocemos): mandan los bytes.
        var sniffed = Sniff(bytes);
        if (sniffed is not null)
        {
            return sniffed;
        }

        return mime is not null && mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? ".png" : null;
    }

    private static string? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G')
            return ".png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            return ".jpg";
        if (b.Length >= 6 && b[..4].SequenceEqual("GIF8"u8))
            return ".gif";
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8))
            return ".webp";
        if (b.Length >= 12 && b[4..8].SequenceEqual("ftyp"u8) &&
            (b[8..12].SequenceEqual("heic"u8) || b[8..12].SequenceEqual("heix"u8) || b[8..12].SequenceEqual("mif1"u8)))
            return ".heic";
        if (b.Length >= 2 && b[0] == (byte)'B' && b[1] == (byte)'M')
            return ".bmp";
        return null;
    }

#if ANDROID
    /// <summary>
    /// Lee la imagen de un <c>content://</c>. <c>null</c> si no se puede leer o no es una imagen.
    /// </summary>
    /// <param name="hint">El tipo que traia el portapapeles o el teclado, por si el proveedor no da ninguno.</param>
    public static (byte[] Bytes, string Extension)? Read(Android.Content.Context context, Android.Net.Uri uri, string? hint)
    {
        try
        {
            var resolver = context.ContentResolver;
            var mime = resolver?.GetType(uri) ?? hint;
            using var stream = resolver?.OpenInputStream(uri);
            if (stream is null)
            {
                return null;
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            var extension = Extension(mime, bytes);
            return extension is null ? null : (bytes, extension);
        }
        catch (Exception ex) when (ex is Java.Lang.SecurityException or IOException or Java.IO.IOException)
        {
            // Sin permiso para leerla (el proveedor no lo concedio) o ya no existe: como si no hubiera.
            return null;
        }
    }

    /// <summary>El primer tipo de imagen que anuncia el ClipData, si anuncia alguno.</summary>
    public static string? ImageMime(Android.Content.ClipDescription? description)
    {
        if (description is null)
        {
            return null;
        }

        for (var i = 0; i < description.MimeTypeCount; i++)
        {
            var mime = description.GetMimeType(i);
            if (mime is not null && mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return mime;
            }
        }

        return null;
    }
#endif
}
