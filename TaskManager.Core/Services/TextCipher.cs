using System.Security.Cryptography;
using System.Text;

namespace TaskManager.Core.Services;

/// <summary>
/// Cifra el <b>texto libre</b> antes de que salga hacia el servidor y lo descifra al volver.
/// </summary>
/// <remarks>
/// <para><b>Que se cifra.</b> Solo texto que escribe el usuario: titulos, notas, etiquetas, nombres
/// de lista, titulos de paso y el nombre y la direccion de los adjuntos. <b>No</b> se tocan las
/// fechas, los booleanos, los numeros ni los identificadores: <c>updated_at</c> y <c>synced_at</c>
/// son lo que decide que baja y que gana en un conflicto, y <c>owner_id</c>, <c>list_id</c> y
/// <c>group_id</c> son lo que la RLS mira para dejar ver una fila. Cifrarlos no dejaria la
/// aplicacion mas discreta: la dejaria rota.</para>
///
/// <para><b>La base local se queda en claro.</b> Se cifra al subir y se descifra al bajar. Si se
/// guardara cifrado tambien aqui, el buscador, los filtros y el orden tendrian que descifrar la
/// tabla entera en cada pulsacion.</para>
///
/// <para><b>Hasta donde llega.</b> La clave sale del identificador del usuario (o del grupo), que
/// es un dato que el servidor tambien conoce. Esto esconde el texto de quien mire la tabla por
/// encima o se lleve una copia suelta, pero <b>no</b> de quien tenga a la vez la base y el
/// identificador. Para eso haria falta una clave que el servidor no vea nunca —una contraseña que
/// el usuario escriba en cada dispositivo—, y eso es otra decision: la que se pidio es esta.</para>
///
/// <para><b>Formato.</b> <c>enc1:</c> + base64(nonce[12] | etiqueta[16] | texto cifrado), con
/// AES-256-GCM. El prefijo es lo que permite convivir con lo que ya estaba guardado en claro: sin
/// el, la primera bajada no sabria si aquello habia que descifrarlo o no.</para>
/// </remarks>
public sealed class TextCipher
{
    /// <summary>Marca de version. Si algun dia cambia el algoritmo, cambia el prefijo.</summary>
    private const string Prefix = "enc1:";

    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>
    /// Sal fija del derivado. No es un secreto —esta aqui escrita—: sirve para que la misma clave
    /// no valga en otra aplicacion que derivara del mismo identificador.
    /// </summary>
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("TaskManager.text.v1");

    /// <summary>
    /// Claves ya derivadas. El derivado es lento a proposito y los identificadores son dos o tres,
    /// asi que se hace una vez por identificador y no una por campo.
    /// </summary>
    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

    /// <summary>
    /// Si esta plataforma sabe hacer AES-GCM. Se comprueba en vez de darlo por hecho: donde no lo
    /// hubiera, <see cref="Protect"/> devolveria el texto tal cual y la sincronizacion seguiria
    /// funcionando en claro en vez de reventar en cada subida.
    /// </summary>
    public static bool IsAvailable => AesGcm.IsSupported;

    /// <summary>Si el texto guardado esta cifrado por aqui.</summary>
    public static bool IsEncrypted(string? stored) =>
        stored is not null && stored.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Cifra con la clave del identificador dado. El vacio se queda vacio: cifrarlo no esconde nada
    /// —que un campo este vacio se ve igual por su longitud— y complica la vuelta.
    /// </summary>
    public string Protect(string? text, string keyId)
    {
        if (string.IsNullOrEmpty(text) || keyId.Length == 0 || IsEncrypted(text) || !IsAvailable)
        {
            return text ?? string.Empty;
        }

        var key = KeyFor(keyId);
        var plain = Encoding.UTF8.GetBytes(text);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plain, cipher, tag);
        }

        var packed = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(packed, 0);
        tag.CopyTo(packed, NonceSize);
        cipher.CopyTo(packed, NonceSize + TagSize);

        return Prefix + Convert.ToBase64String(packed);
    }

    /// <summary>
    /// Descifra probando las claves que se le pasan.
    /// </summary>
    /// <remarks>
    /// <para>Se prueban varias porque una tarea puede venir cifrada con el identificador del
    /// usuario o con el del grupo del que cuelga, y al bajarla todavia no siempre se sabe cual de
    /// los dos: su lista puede llegar en el mismo lote. GCM autentica, asi que una clave que no es
    /// falla limpiamente en vez de devolver basura.</para>
    ///
    /// <para>Si ninguna sirve se devuelve lo que vino, tal cual. Es feo de ver, pero es informacion:
    /// dice que esa fila esta cifrada con algo que este dispositivo no conoce todavia. Lo contrario
    /// —devolver vacio— borraria el texto del usuario al guardarlo aqui.</para>
    /// </remarks>
    public string Unprotect(string? stored, IReadOnlyList<string> keyIds)
    {
        if (string.IsNullOrEmpty(stored) || !IsEncrypted(stored) || !IsAvailable)
        {
            return stored ?? string.Empty;
        }

        byte[] packed;
        try
        {
            packed = Convert.FromBase64String(stored[Prefix.Length..]);
        }
        catch (FormatException)
        {
            return stored;
        }

        if (packed.Length < NonceSize + TagSize)
        {
            return stored;
        }

        var nonce = packed.AsSpan(0, NonceSize);
        var tag = packed.AsSpan(NonceSize, TagSize);
        var cipher = packed.AsSpan(NonceSize + TagSize);

        foreach (var keyId in keyIds)
        {
            if (keyId.Length == 0)
            {
                continue;
            }

            var plain = new byte[cipher.Length];
            try
            {
                using var aes = new AesGcm(KeyFor(keyId), TagSize);
                aes.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            catch (CryptographicException)
            {
                // Esa clave no era. Se prueba la siguiente.
            }
        }

        return stored;
    }

    /// <remarks>
    /// PBKDF2 sobre el identificador. Las vueltas no protegen de quien conozca el identificador
    /// —lo tiene entero, no lo esta adivinando—: estan para que la clave no sea el propio
    /// identificador escrito de otra forma, y cuestan una vez por sesion.
    /// </remarks>
    private byte[] KeyFor(string keyId)
    {
        if (_keys.TryGetValue(keyId, out var cached))
        {
            return cached;
        }

        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(keyId.ToLowerInvariant()),
            Salt,
            iterations: 100_000,
            HashAlgorithmName.SHA256,
            outputLength: 32);

        _keys[keyId] = key;
        return key;
    }
}
