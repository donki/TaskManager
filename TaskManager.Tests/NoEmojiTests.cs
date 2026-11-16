using System.Reflection;
using System.Text.Json;
using TaskManager.Core.Services;

namespace TaskManager.Tests;

/// <summary>
/// Ningun emoji ni simbolo dibujado como texto en la interfaz (constitucion General 6.2): cada
/// telefono los pinta a su manera, en color, se cortan con la letra grande y no siguen el tema.
/// Los iconos son SVG planos (Android) o glifos de la fuente de iconos (Windows). Nacio del 📌 de
/// las tareas fijadas, visto en un Xiaomi el 2026-11-16.
/// </summary>
public class NoEmojiTests
{
    /// <summary>
    /// Pictogramas (U+1F000…U+1FFFF), simbolos varios y dingbats (U+2600…U+27BF), flechas y
    /// tecnicos (U+2190…U+23FF), figuras geometricas (U+25A0…U+25FF), U+2B00…U+2BFF y el selector
    /// de variante emoji (U+FE0F).
    /// </summary>
    internal static bool IsPictograph(int c) =>
        c is >= 0x1F000 and <= 0x1FFFF
            or >= 0x2600 and <= 0x27BF
            or >= 0x2190 and <= 0x23FF
            or >= 0x25A0 and <= 0x25FF
            or >= 0x2B00 and <= 0x2BFF
            or 0xFE0F;

    internal static List<string> Pictographs(string text)
    {
        var found = new List<string>();
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
        {
            var c = char.ConvertToUtf32(text, i);
            if (IsPictograph(c))
            {
                found.Add(char.ConvertFromUtf32(c));
            }
        }

        return found;
    }

    [Theory]
    [InlineData("\U0001F4CC Pagar", 1)]
    [InlineData("⚠️ Ojo", 2)]
    [InlineData("✓ hecho ○", 2)]
    [InlineData("Uso bajo su propio riesgo · «tal cual» — 3/5 #casa", 0)]
    public void El_detector_ve_los_emoji_y_deja_pasar_el_texto_normal(string text, int expected) =>
        Assert.Equal(expected, Pictographs(text).Count);

    [Theory]
    [InlineData("English")]
    [InlineData("Spanish")]
    public void Ningun_texto_de_la_interfaz_lleva_emoji(string language)
    {
        var texts = (Dictionary<string, string>)typeof(LocalizationService)
            .GetField(language, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var bad = texts.Where(kv => Pictographs(kv.Value).Count > 0).Select(kv => kv.Key).ToList();
        Assert.Empty(bad);
    }

    [Fact]
    public void Las_novedades_no_llevan_emoji()
    {
        var path = Path.Combine(LocalizationTests.RepoRoot(), "TaskManager.Core", "whatsnew.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            foreach (var language in new[] { "es", "en" })
            {
                foreach (var item in entry.GetProperty(language).EnumerateArray())
                {
                    Assert.True(Pictographs(item.GetString()!).Count == 0,
                        $"{entry.GetProperty("version").GetString()} ({language}): {item.GetString()}");
                }
            }
        }
    }

    /// <summary>
    /// Ni en el codigo de las dos aplicaciones: un emoji en un literal acaba en pantalla (asi
    /// llego el 📌 a las filas). Tambien se miran los comentarios, que es mas simple que separarlos
    /// y no cuesta nada escribirlos sin simbolos.
    /// </summary>
    [Theory]
    [InlineData("TaskManager.Mobile")]
    [InlineData("TaskManager.Desktop")]
    [InlineData("TaskManager.Core")]
    public void El_codigo_de_las_aplicaciones_no_lleva_emoji(string project)
    {
        var root = Path.Combine(LocalizationTests.RepoRoot(), project);
        var bad = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Contains("obj") || parts.Contains("bin")
                || !(file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".xaml", StringComparison.Ordinal)))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (Pictographs(lines[i]).Count > 0)
                {
                    bad.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }
        }

        Assert.Empty(bad);
    }
}
