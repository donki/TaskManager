using TaskManager.Core.Services;

namespace TaskManager.Tests;

public class TextCipherTests
{
    private const string User = "2b1a7c0e-0000-4000-8000-000000000001";
    private const string Group = "9f3d5e11-0000-4000-8000-000000000002";

    [Fact]
    public void RoundTrip_WithTheSameKey()
    {
        var cipher = new TextCipher();
        var stored = cipher.Protect("Comprar pan y leche ñ€", User);

        Assert.True(TextCipher.IsEncrypted(stored));
        Assert.DoesNotContain("pan", stored);
        Assert.Equal("Comprar pan y leche ñ€", new TextCipher().Unprotect(stored, [User]));
    }

    [Fact]
    public void KeyIdIsCaseInsensitive() =>
        Assert.Equal("hola", new TextCipher().Unprotect(new TextCipher().Protect("hola", User.ToUpperInvariant()), [User]));

    [Fact]
    public void SameTextTwice_GivesDifferentCiphertexts()
    {
        var cipher = new TextCipher();
        Assert.NotEqual(cipher.Protect("x", User), cipher.Protect("x", User));
    }

    [Fact]
    public void Unprotect_TriesEveryCandidateKey()
    {
        var stored = new TextCipher().Protect("de grupo", Group);
        Assert.Equal("de grupo", new TextCipher().Unprotect(stored, ["", User, Group]));
    }

    [Fact]
    public void WrongKey_ReturnsStoredText_NeverEmpty()
    {
        var stored = new TextCipher().Protect("secreto", User);
        Assert.Equal(stored, new TextCipher().Unprotect(stored, [Group]));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("texto en claro", "texto en claro")]
    [InlineData("enc1:@@no-es-base64@@", "enc1:@@no-es-base64@@")]
    [InlineData("enc1:AAAA", "enc1:AAAA")]
    public void Unprotect_PassesThroughWhatItCannotRead(string? stored, string expected) =>
        Assert.Equal(expected, new TextCipher().Unprotect(stored, [User]));

    [Fact]
    public void Protect_LeavesEmptyAlreadyEncryptedOrKeyless()
    {
        var cipher = new TextCipher();
        Assert.Equal(string.Empty, cipher.Protect(null, User));
        Assert.Equal(string.Empty, cipher.Protect("", User));
        Assert.Equal("sin clave", cipher.Protect("sin clave", ""));

        var once = cipher.Protect("una vez", User);
        Assert.Equal(once, cipher.Protect(once, User));
    }

    [Fact]
    public void TamperedCiphertext_IsRejected()
    {
        var stored = new TextCipher().Protect("importante", User);
        var bytes = Convert.FromBase64String(stored["enc1:".Length..]);
        bytes[^1] ^= 0xFF;
        var tampered = "enc1:" + Convert.ToBase64String(bytes);

        Assert.Equal(tampered, new TextCipher().Unprotect(tampered, [User]));
    }

    [Fact]
    public void IsAvailable_OnThisPlatform() => Assert.True(TextCipher.IsAvailable);
}

public class GroupCryptoTests
{
    [Fact]
    public void RoundTrip()
    {
        var salt = GroupCrypto.CreateSalt();
        var key = GroupCrypto.DeriveKey("frase compartida", salt);
        Assert.Equal(32, key.Length);

        var enc = GroupCrypto.Encrypt("Tarea del grupo", key);
        Assert.True(GroupCrypto.IsEncrypted(enc));
        Assert.StartsWith("v1.", enc);
        Assert.True(GroupCrypto.TryDecrypt(enc, key, out var plain));
        Assert.Equal("Tarea del grupo", plain);
    }

    [Fact]
    public void SamePhrase_DifferentSalt_DifferentKey()
    {
        var a = GroupCrypto.DeriveKey("frase", GroupCrypto.CreateSalt());
        var b = GroupCrypto.DeriveKey("frase", GroupCrypto.CreateSalt());
        Assert.NotEqual(a, b);

        var salt = GroupCrypto.CreateSalt();
        Assert.Equal(GroupCrypto.DeriveKey("frase", salt), GroupCrypto.DeriveKey("frase", salt));
    }

    [Fact]
    public void EmptyPhrase_Throws() =>
        Assert.Throws<ArgumentException>(() => GroupCrypto.DeriveKey("", GroupCrypto.CreateSalt()));

    [Fact]
    public void WrongKeyOrTampered_Fails()
    {
        var salt = GroupCrypto.CreateSalt();
        var key = GroupCrypto.DeriveKey("buena", salt);
        var other = GroupCrypto.DeriveKey("mala", salt);
        var enc = GroupCrypto.Encrypt("hola", key);

        Assert.False(GroupCrypto.TryDecrypt(enc, other, out var plain));
        Assert.Equal(string.Empty, plain);

        var parts = enc.Split('.');
        var payload = Convert.FromBase64String(parts[2]);
        payload[0] ^= 1;
        Assert.False(GroupCrypto.TryDecrypt($"v1.{parts[1]}.{Convert.ToBase64String(payload)}", key, out _));
    }

    [Theory]
    [InlineData("v1.solo-dos")]
    [InlineData("v1.***.***")]
    [InlineData("v1.AAAA.AAAAAAAAAAAAAAAAAAAAAA==")]
    public void MalformedEnvelope_Fails(string value)
    {
        var key = new byte[32];
        Assert.False(GroupCrypto.TryDecrypt(value, key, out _));
    }

    [Fact]
    public void PlainOrEmpty_PassesThrough()
    {
        var key = new byte[32];
        Assert.True(GroupCrypto.TryDecrypt("", key, out var empty));
        Assert.Equal(string.Empty, empty);
        Assert.True(GroupCrypto.TryDecrypt("de antes", key, out var old));
        Assert.Equal("de antes", old);
        Assert.Equal(string.Empty, GroupCrypto.Encrypt("", key));
        Assert.False(GroupCrypto.IsEncrypted(null));
        Assert.False(GroupCrypto.IsEncrypted("v2.x.y"));
    }
}

public class GroupLinkTests
{
    [Fact]
    public void For_Then_Read_RoundTripsWithSpecialCharacters()
    {
        var invite = new GroupInvite("ABC&12", "clave con espacios=+/?");
        var link = GroupLink.For(invite);

        Assert.Equal("taskmanager", link.Scheme);
        Assert.Equal(invite, GroupLink.Read(link));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://join?code=A&key=B")]
    [InlineData("taskmanager://join?code=A")]
    [InlineData("taskmanager://join?key=B")]
    [InlineData("taskmanager://join?code=&key=B")]
    [InlineData("taskmanager://join")]
    public void Read_RejectsForeignOrIncompleteLinks(string? link) =>
        Assert.Null(GroupLink.Read(link is null ? null : new Uri(link)));

    [Fact]
    public void Read_IgnoresUnknownParameters_AndSchemeCase() =>
        Assert.Equal(new GroupInvite("A", "B"), GroupLink.Read(new Uri("TASKMANAGER://join?x=1&code=A&novalue&key=B")));

    [Fact]
    public void QrPng_IsAPng()
    {
        var png = GroupLink.QrPng(GroupLink.For(new GroupInvite("ABC123", GroupInvite.NewKey())), 2);
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, png[..4]);
    }

    [Fact]
    public async Task Message_ContainsEverythingInBothLanguages()
    {
        var invite = new GroupInvite("ABC123", "k-e-y");
        foreach (var language in new[] { "es", "en" })
        {
            await using var store = await TestStore.CreateAsync(language: language);
            var text = GroupLink.Message(store.Texts, "Casa", invite);
            Assert.Contains("Casa", text);
            Assert.Contains("ABC123", text);
            Assert.Contains("k-e-y", text);
            Assert.Contains(GroupLink.For(invite).ToString(), text);
        }
    }

    [Fact]
    public void NewKey_IsAGuid() => Assert.True(Guid.TryParse(GroupInvite.NewKey(), out _));
}
