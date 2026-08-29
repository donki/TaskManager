namespace TaskManager.Core.Gamification;

/// <summary>
/// Reglas de puntuacion. Un solo sitio para que movil y escritorio premien exactamente igual.
/// </summary>
public static class XpRules
{
    public const int Task = 50;
    public const int Step = 10;
    public const int Breakdown = 15;

    /// <summary>Ventana para encadenar combo, en segundos.</summary>
    public const int ComboWindowSeconds = 90;

    /// <summary>Multiplicadores de racha corta. El tope es x3 (especificacion 4.A).</summary>
    public static readonly double[] ComboSteps = [1.0, 1.5, 2.0, 3.0];

    public static double ComboFor(int chain) =>
        ComboSteps[Math.Clamp(chain, 0, ComboSteps.Length - 1)];
}

/// <summary>
/// Curva de nivel: XP(n) = 100 * n * (n + 1) / 2. Subir cuesta cada vez un poco mas sin volverse
/// inalcanzable.
/// </summary>
public static class LevelCurve
{
    public static int XpForLevel(int level) => level <= 1 ? 0 : 100 * (level - 1) * level / 2;

    public static int LevelFor(int totalXp)
    {
        var level = 1;
        while (XpForLevel(level + 1) <= totalXp)
        {
            level++;
        }

        return level;
    }

    /// <summary>Progreso dentro del nivel actual, de 0 a 1.</summary>
    public static double ProgressInLevel(int totalXp)
    {
        var level = LevelFor(totalXp);
        var start = XpForLevel(level);
        var next = XpForLevel(level + 1);
        return next == start ? 1 : (double)(totalXp - start) / (next - start);
    }

    public static int XpToNextLevel(int totalXp) => XpForLevel(LevelFor(totalXp) + 1) - totalXp;
}

/// <summary>
/// Recompensas estéticas. No cambian el juego: solo desbloquean adornos (especificacion 4.B).
/// </summary>
public sealed record Unlockable(int Level, string Key, string Name);

public static class Unlockables
{
    public static readonly IReadOnlyList<Unlockable> All =
    [
        new(2,  "confetti_classic", "Confeti clasico"),
        new(3,  "theme_ocean",      "Tema Oceano"),
        new(5,  "confetti_stars",   "Confeti de estrellas"),
        new(7,  "theme_forest",     "Tema Bosque"),
        new(10, "badge_guild",      "Insignia del gremio"),
        new(12, "confetti_fireworks", "Confeti de fuegos artificiales"),
        new(15, "theme_sunset",     "Tema Atardecer"),
    ];

    public static IEnumerable<Unlockable> UnlockedAt(int level) => All.Where(u => u.Level <= level);

    public static Unlockable? NextAfter(int level) => All.FirstOrDefault(u => u.Level > level);
}

/// <summary>Lo que la interfaz necesita saber para celebrar: XP, combo y si se ha subido de nivel.</summary>
public sealed record Celebration(
    int Xp,
    double Combo,
    int TotalXp,
    int Level,
    bool LeveledUp,
    Unlockable? Unlocked)
{
    public bool IsCombo => Combo > 1.0;
}
