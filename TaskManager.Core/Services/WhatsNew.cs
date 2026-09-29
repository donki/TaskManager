using System.Globalization;
using System.Text.Json;

namespace TaskManager.Core.Services;

/// <summary>
/// Novedades de las ultimas versiones (constitucion General 6.7): lo que nota el usuario, en los
/// dos idiomas, sacado del CHANGELOG a mano y guardado en <c>whatsnew.json</c>.
/// </summary>
/// <remarks>
/// Vive en el nucleo, como un recurso incrustado, porque el movil y el escritorio enseñan las
/// mismas novedades: tenerlas por duplicado acabaria con las dos diciendo cosas distintas.
/// </remarks>
public static class WhatsNew
{
    /// <summary>Cuantas versiones se enseñan (General 6.7: las cinco ultimas).</summary>
    public const int MaxVersions = 5;

    public sealed record Release(string Version, DateTime? Date, IReadOnlyList<string> Items);

    /// <summary>
    /// Las ultimas versiones, de la mas nueva a la mas antigua, en <paramref name="language"/>
    /// (<c>es</c> o <c>en</c>; si falta ese idioma, en ingles). Si el fichero no se puede leer
    /// devuelve la lista vacia: las novedades nunca tiran la aplicacion.
    /// </summary>
    public static IReadOnlyList<Release> Load(string language)
    {
        try
        {
            using var stream = typeof(WhatsNew).Assembly.GetManifestResourceStream("TaskManager.Core.whatsnew.json");
            if (stream is null)
            {
                return [];
            }

            using var doc = JsonDocument.Parse(stream);
            var list = new List<Release>();
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var items = entry.TryGetProperty(language, out var arr) || entry.TryGetProperty("en", out arr)
                    ? arr.EnumerateArray().Select(i => i.GetString() ?? string.Empty).Where(i => i.Length > 0).ToList()
                    : [];

                DateTime? date = entry.TryGetProperty("date", out var d)
                    && DateTime.TryParseExact(d.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed
                    : null;

                list.Add(new Release(entry.GetProperty("version").GetString() ?? string.Empty, date, items));
            }

            return [.. list.Take(MaxVersions)];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WhatsNew.Load: {ex}");
            return [];
        }
    }

    /// <summary>
    /// Dos versiones son la misma aunque una venga con ceros y otra sin ellos: el movil dice
    /// <c>2026.09.29.00</c> y el escritorio <c>2026.9.29.0</c>.
    /// </summary>
    public static bool SameVersion(string a, string b) =>
        Version.TryParse(a, out var va) && Version.TryParse(b, out var vb)
            ? va == vb
            : string.Equals(a, b, StringComparison.Ordinal);
}
