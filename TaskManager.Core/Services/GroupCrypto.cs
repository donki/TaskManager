using System.Security.Cryptography;
using System.Text;

namespace TaskManager.Core.Services;

/// <summary>
/// Cifrado de extremo a extremo con la clave compartida del grupo.
/// </summary>
/// <remarks>
/// Lo que se sube a Supabase va cifrado con una clave que **el servidor no tiene**: Supabase guarda
/// y sincroniza cadenas opacas. La RLS sigue estando y es la primera barrera —sin pertenencia no se
/// leen las filas—, pero ya no es la unica: aunque alguien se saltara la RLS, o mirara la base de
/// datos por detras, no leeria ni el titulo de una tarea.
///
/// <para><b>Que se cifra y que no.</b> Se cifra el contenido que escribe la persona: titulos de
/// tarea, notas, micro-pasos y nombres de lista. **No** se cifran las fechas, los estados
/// (hecha/pendiente), los identificadores ni la pertenencia: hacen falta en claro para que la base
/// de datos pueda indexar, filtrar por "Mi Dia" y resolver la RLS. Es decir, el servidor sabe
/// cuantas tareas hay, cuando se crearon y quien las completo, pero no que dicen.</para>
///
/// <para><b>Derivacion.</b> La clave compartida es una frase que elige una persona, asi que no se
/// usa tal cual: se pasa por PBKDF2-HMAC-SHA256 con la sal del grupo y 600.000 iteraciones (la
/// recomendacion de OWASP para 2023 en adelante). Asi dos grupos con la misma frase tienen claves
/// distintas y probar frases a lo bruto sale caro.</para>
///
/// <para><b>Cifrado.</b> AES-256-GCM, que ademas de cifrar autentica: si alguien cambia un byte del
/// texto cifrado, el descifrado falla en vez de devolver basura. Cada valor lleva su propio nonce
/// aleatorio de 96 bits, que es lo que exige GCM para no repetir nunca (nonce, clave).</para>
/// </remarks>
public static class GroupCrypto
{
    /// <summary>Version del sobre. Va delante para poder cambiar de algoritmo sin romper lo viejo.</summary>
    private const string Envelope = "v1";

    private const int KeySizeBytes = 32;     // AES-256
    private const int NonceSizeBytes = 12;   // 96 bits, lo estandar en GCM
    private const int TagSizeBytes = 16;
    private const int SaltSizeBytes = 16;

    /// <summary>Iteraciones de PBKDF2. Subirlas obliga a cambiar la version del sobre.</summary>
    private const int Iterations = 600_000;

    /// <summary>Sal nueva para un grupo. Es publica: se guarda junto al grupo y se reparte al entrar.</summary>
    public static string CreateSalt() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltSizeBytes));

    /// <summary>
    /// Clave de cifrado del grupo a partir de la frase compartida y la sal. Es cara a proposito
    /// (cientos de milisegundos): se hace una vez al entrar y se guarda en el almacen seguro del
    /// dispositivo, no en cada operacion.
    /// </summary>
    public static byte[] DeriveKey(string sharedKey, string saltBase64)
    {
        if (string.IsNullOrEmpty(sharedKey))
            throw new ArgumentException("La clave compartida no puede estar vacia.", nameof(sharedKey));

        var salt = Convert.FromBase64String(saltBase64);
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(sharedKey),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySizeBytes);
    }

    /// <summary>
    /// Cifra un texto. Devuelve <c>v1.&lt;nonce&gt;.&lt;cifrado+tag&gt;</c> en base64, que es lo que
    /// viaja a la columna de Supabase. Un texto vacio se queda vacio: cifrar la nada solo gasta
    /// espacio y delata la longitud.
    /// </summary>
    public static string Encrypt(string plainText, byte[] key)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var plain = Encoding.UTF8.GetBytes(plainText);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(nonce, plain, cipher, tag);

        // El tag se pega detras del texto cifrado: es un solo campo que guardar y que transportar.
        var payload = new byte[cipher.Length + tag.Length];
        cipher.CopyTo(payload, 0);
        tag.CopyTo(payload, cipher.Length);

        return $"{Envelope}.{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(payload)}";
    }

    /// <summary>
    /// Descifra lo que venga del servidor. Si el sobre no cuadra, la clave no es la del grupo o el
    /// contenido esta manipulado, devuelve false: el que llama decide que ensenar, pero nunca se
    /// muestra texto inventado.
    /// </summary>
    public static bool TryDecrypt(string value, byte[] key, out string plainText)
    {
        plainText = string.Empty;

        if (string.IsNullOrEmpty(value))
            return true;

        // Contenido de antes de cifrar (o de una lista privada): se deja pasar tal cual.
        if (!value.StartsWith(Envelope + ".", StringComparison.Ordinal))
        {
            plainText = value;
            return true;
        }

        var parts = value.Split('.', 3);
        if (parts.Length != 3)
            return false;

        try
        {
            var nonce = Convert.FromBase64String(parts[1]);
            var payload = Convert.FromBase64String(parts[2]);
            if (nonce.Length != NonceSizeBytes || payload.Length < TagSizeBytes)
                return false;

            var cipher = payload.AsSpan(0, payload.Length - TagSizeBytes);
            var tag = payload.AsSpan(payload.Length - TagSizeBytes);
            var plain = new byte[cipher.Length];

            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, cipher, tag, plain);

            plainText = Encoding.UTF8.GetString(plain);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            // Clave equivocada o dato manipulado. No es un fallo de programa: es exactamente lo que
            // AES-GCM tiene que hacer.
            return false;
        }
    }

    /// <summary>Si un valor viene cifrado, para saber si hace falta la clave antes de pintarlo.</summary>
    public static bool IsEncrypted(string? value) =>
        value is not null && value.StartsWith(Envelope + ".", StringComparison.Ordinal);
}
