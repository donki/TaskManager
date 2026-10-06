using TaskManager.Core.Data;

namespace TaskManager.Core.Models;

/// <summary>
/// El filtro por etiquetas de las listas: ninguna, una o varias a la vez.
/// </summary>
/// <remarks>
/// <para>Se sigue guardando y pasando como <b>una sola cadena</b>, la misma que antes llevaba una
/// etiqueta suelta: varias van separadas por comas (<c>casa,obra</c>), que es un caracter que no
/// puede tener una etiqueta (<see cref="TaskTags.FromInput"/> parte por ahi). Asi lo guardado antes
/// —una etiqueta— se sigue leyendo igual, y los ajustes, el repositorio y las pantallas no han
/// tenido que cambiar de forma.</para>
///
/// <para>Con varias, una tarea entra si lleva <b>cualquiera</b> de ellas (O, no Y). Es lo que se
/// espera de marcar varias pastillas con Ctrl —«lo de casa y lo de la obra»— y con «Sin etiqueta»
/// marcada a la vez se suman tambien las que no llevan ninguna.</para>
///
/// <para><c>null</c> es «Todas»: sin filtro.</para>
/// </remarks>
public static class TagFilter
{
    private const char Separator = ',';

    /// <summary>Las etiquetas del filtro, sin repetir. Vacio si no hay filtro.</summary>
    public static IReadOnlyList<string> Parse(string? filter) =>
        string.IsNullOrEmpty(filter)
            ? []
            : filter.Split(Separator, StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

    /// <summary>El filtro a partir de sus etiquetas; <c>null</c> si no queda ninguna.</summary>
    public static string? Build(IEnumerable<string> tags)
    {
        var clean = tags.Where(t => !string.IsNullOrEmpty(t))
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .ToList();

        return clean.Count == 0 ? null : string.Join(Separator, clean);
    }

    /// <summary>
    /// Si la pastilla de <paramref name="tag"/> va encendida. La de «Todas» (<c>null</c>) solo
    /// cuando no hay filtro.
    /// </summary>
    public static bool Has(string? filter, string? tag) =>
        tag is null
            ? string.IsNullOrEmpty(filter)
            : Parse(filter).Contains(tag, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>
    /// Ctrl+clic: pone o quita <paramref name="tag"/> sin tocar las demas. «Todas» lo borra todo,
    /// igual que un clic normal.
    /// </summary>
    public static string? Toggle(string? filter, string? tag)
    {
        if (tag is null)
        {
            return null;
        }

        var tags = Parse(filter).ToList();

        if (tags.RemoveAll(t => string.Equals(t, tag, StringComparison.CurrentCultureIgnoreCase)) == 0)
        {
            tags.Add(tag);
        }

        return Build(tags);
    }

    /// <summary>
    /// Lo que deja un clic en una pastilla: sin Ctrl, esa sola (como siempre); con Ctrl, la suma o
    /// la quita de las que ya estaban.
    /// </summary>
    public static string? Click(string? filter, string? tag, bool ctrl) =>
        ctrl ? Toggle(filter, tag) : tag;

    /// <summary>
    /// Lo que queda del filtro al quitarle <paramref name="tag"/>: al borrar la etiqueta, o
    /// cuando ya no la lleva ninguna tarea.
    /// </summary>
    public static string? Without(string? filter, string tag) =>
        Build(Parse(filter).Where(t => !string.Equals(t, tag, StringComparison.CurrentCultureIgnoreCase)));

    /// <summary>
    /// Quita del filtro las etiquetas que ya no existen. «Sin etiqueta» se queda siempre.
    /// </summary>
    public static string? Prune(string? filter, IEnumerable<string> existing)
    {
        var known = existing.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        return Build(Parse(filter).Where(t => t == TaskRepository.NoTag || known.Contains(t)));
    }

    /// <summary>Si la tarea entra en el filtro: lleva alguna de sus etiquetas.</summary>
    public static bool Matches(string? filter, IReadOnlyList<string> taskTags)
    {
        var tags = Parse(filter);

        if (tags.Count == 0)
        {
            return true;
        }

        return tags.Any(t => t == TaskRepository.NoTag
            ? taskTags.Count == 0
            : taskTags.Contains(t, StringComparer.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// Como se escribe el filtro en el rotulo de la lista: <c>#casa · #obra</c>. Vacio si no hay.
    /// </summary>
    public static string Describe(string? filter, string noTagText) =>
        string.Join(" · ", Parse(filter).Select(t => t == TaskRepository.NoTag ? noTagText : $"#{t}"));
}
