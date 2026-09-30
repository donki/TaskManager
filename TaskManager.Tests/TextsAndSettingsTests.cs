using System.Reflection;
using System.Text.RegularExpressions;
using TaskManager.Core.Models;
using TaskManager.Core.Services;

namespace TaskManager.Tests;

public partial class LocalizationTests
{
    private static Dictionary<string, string> Dictionary(string name) =>
        (Dictionary<string, string>)typeof(LocalizationService)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    private static readonly Dictionary<string, string> English = Dictionary("English");
    private static readonly Dictionary<string, string> Spanish = Dictionary("Spanish");

    [Fact]
    public void SpanishAndEnglish_HaveExactlyTheSameKeys()
    {
        Assert.Empty(English.Keys.Except(Spanish.Keys));
        Assert.Empty(Spanish.Keys.Except(English.Keys));
    }

    [Fact]
    public void NoTextIsEmpty()
    {
        Assert.DoesNotContain(English, kv => string.IsNullOrWhiteSpace(kv.Value));
        Assert.DoesNotContain(Spanish, kv => string.IsNullOrWhiteSpace(kv.Value));
    }

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    /// <summary>
    /// Un «{2}» que falta en una traduccion no revienta, pero deja fuera un dato; uno de mas
    /// revienta con FormatException al pintar. Los dos idiomas tienen que pedir los mismos.
    /// </summary>
    [Fact]
    public void Placeholders_MatchBetweenLanguages()
    {
        static string Set(string text) =>
            string.Join(",", Placeholder().Matches(text.Replace("{{", "").Replace("}}", ""))
                .Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order());

        var mismatched = English.Keys
            .Where(k => Set(English[k]) != Set(Spanish[k]))
            .Select(k => $"{k}: en[{Set(English[k])}] es[{Set(Spanish[k])}]")
            .ToList();

        Assert.Empty(mismatched);
    }

    /// <summary>
    /// Cada clave que piden las pantallas del movil y de Windows existe. Si falta, la interfaz
    /// enseña la clave a pelo («SettingsTitle») en vez del texto.
    /// </summary>
    [Fact]
    public void EveryKeyUsedByTheApps_Exists()
    {
        var root = RepoRoot();
        var patterns = new[]
        {
            new Regex(@"\{loc:T\s+(?:Key=)?([A-Za-z][A-Za-z0-9_]*)"),
            new Regex(@"(?:Loc\.Instance|\bloc|\bLoc|\btextos|\btexts|\bTextos|\bTexts|\b_texts|\b_loc|\bt)\s*\[\s*""([A-Za-z][A-Za-z0-9_]*)""\s*\]"),
            new Regex(@"\.Format\(\s*""([A-Z][A-Za-z0-9_]*)"""),
        };

        var files = new[] { "TaskManager.Mobile", "TaskManager.Desktop", "TaskManager.Core" }
            .SelectMany(d => SourceFiles(Path.Combine(root, d)));

        var used = new SortedSet<string>();
        foreach (var file in files)
        {
            // Sin comentarios: la documentacion pone ejemplos como «{loc:T Clave}».
            var text = string.Join('\n', File.ReadLines(file).Where(l => !l.TrimStart().StartsWith("//")));
            foreach (var pattern in patterns)
            {
                foreach (Match m in pattern.Matches(text))
                {
                    used.Add(m.Groups[1].Value);
                }
            }
        }

        Assert.True(used.Count > 100, $"Solo se han encontrado {used.Count} claves: el patron ya no reconoce como se piden.");
        Assert.Empty(used.Where(k => !English.ContainsKey(k)));
    }

    /// <summary>Los .cs y .xaml de una carpeta, sin bajar a bin ni obj (que son enormes).</summary>
    private static IEnumerable<string> SourceFiles(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            if (file.EndsWith(".cs") || file.EndsWith(".xaml"))
            {
                yield return file;
            }
        }

        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            if (name is "bin" or "obj" or "Resources")
            {
                continue;
            }

            foreach (var file in SourceFiles(sub))
            {
                yield return file;
            }
        }
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaskManager.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No se encuentra TaskManager.slnx");
    }

    [Fact]
    public async Task Indexer_FollowsLanguage_AndFallsBackToKey()
    {
        await using var store = await TestStore.CreateAsync(language: "es");
        var raised = 0;
        store.Texts.LanguageChanged += (_, _) => raised++;

        Assert.Equal("es", store.Texts.Language);
        Assert.Equal("Mis tareas", store.Texts["MenuMyTasks"]);
        Assert.Equal("ClaveQueNoExiste", store.Texts["ClaveQueNoExiste"]);
        Assert.Equal("Cada 3 días", store.Texts.Format("RepeatDailyN", 3));

        await store.Texts.SetLanguageAsync("en");
        Assert.Equal("My tasks", store.Texts["MenuMyTasks"]);
        Assert.Equal(1, raised);

        // Algo que no es es/en = seguir al sistema, que acaba en uno de los dos.
        await store.Texts.SetLanguageAsync("fr");
        Assert.Equal(string.Empty, store.Settings.Get(SettingsService.KeyLanguage));
        Assert.Contains(store.Texts.Language, new[] { "es", "en" });
    }
}

public class WhatsNewTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Load_GivesAtMostFiveReleases_NewestFirst_WithItems(string language)
    {
        var releases = WhatsNew.Load(language);
        Assert.NotEmpty(releases);
        Assert.True(releases.Count <= WhatsNew.MaxVersions);
        Assert.All(releases, r =>
        {
            Assert.True(Version.TryParse(r.Version, out _), r.Version);
            Assert.NotNull(r.Date);
            Assert.NotEmpty(r.Items);
        });

        var versions = releases.Select(r => Version.Parse(r.Version)).ToList();
        Assert.Equal(versions.OrderDescending(), versions);
    }

    [Fact]
    public void Load_UnknownLanguage_FallsBackToEnglish() =>
        Assert.Equal(WhatsNew.Load("en")[0].Items, WhatsNew.Load("de")[0].Items);

    [Fact]
    public void Load_SpanishAndEnglish_SayDifferentThings() =>
        Assert.NotEqual(WhatsNew.Load("en")[0].Items, WhatsNew.Load("es")[0].Items);

    /// <summary>Todas las entradas del fichero traen los dos idiomas con el mismo numero de puntos.</summary>
    [Fact]
    public void EveryEntry_HasBothLanguages()
    {
        var path = Path.Combine(LocalizationTests.RepoRoot(), "TaskManager.Core", "whatsnew.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var es = entry.GetProperty("es").GetArrayLength();
            var en = entry.GetProperty("en").GetArrayLength();
            Assert.True(es > 0 && es == en, entry.GetProperty("version").GetString());
        }
    }

    [Theory]
    [InlineData("2026.09.29.00", "2026.9.29.0", true)]
    [InlineData("2026.09.29.01", "2026.9.29.0", false)]
    [InlineData("", "", true)]
    [InlineData("", "2026.9.29.0", false)]
    [InlineData("beta", "beta", true)]
    public void SameVersion(string a, string b, bool expected) => Assert.Equal(expected, WhatsNew.SameVersion(a, b));
}

public class MailTests
{
    [Fact]
    public void ToTaskTitle_UsesSubjectOrSender()
    {
        var date = new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal("Factura", new MailMessage(1, "a@b.c", "  Factura ", "", date, true).ToTaskTitle());
        Assert.Equal("Correo de a@b.c", new MailMessage(1, "a@b.c", "  ", "", date, true).ToTaskTitle());
    }

    [Fact]
    public void ToTaskContext_IncludesPreviewOnlyWhenThere()
    {
        var date = new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
        var with = new MailMessage(1, "a@b.c", "S", "  primeras lineas ", date, false).ToTaskContext();
        var without = new MailMessage(1, "a@b.c", "S", " ", date, false).ToTaskContext();

        Assert.StartsWith("Correo de a@b.c (", with);
        Assert.EndsWith("\n\nprimeras lineas", with);
        Assert.DoesNotContain("\n", without);
    }

    [Theory]
    [InlineData("x@gmail.com", "imap.gmail.com")]
    [InlineData("x@GoogleMail.com", "imap.gmail.com")]
    [InlineData("x@hotmail.com", "outlook.office365.com")]
    [InlineData("x@yahoo.es", "imap.mail.yahoo.com")]
    [InlineData("x@me.com", "imap.mail.me.com")]
    [InlineData("x@zoho.eu", "imap.zoho.eu")]
    [InlineData("x@empresa.com", "")]
    [InlineData("sinarroba", "")]
    public void ForAddress_PicksPreset(string address, string host)
    {
        var account = MailProviders.ForAddress(address);
        Assert.Equal(address, account.Address);
        Assert.Equal(host, account.ImapHost);
        Assert.Equal(host.Length > 0 && address.Contains('@'), account.IsComplete);
    }

    [Fact]
    public void MailException_KeepsInner()
    {
        var inner = new InvalidOperationException();
        var ex = new MailException("m", inner);
        Assert.Same(inner, ex.InnerException);
    }
}

public class ProgressCaptionTests
{
    [Fact]
    public async Task Footer_WithAndWithoutTasks()
    {
        await using var store = await TestStore.CreateAsync(language: "es");
        Assert.Equal("Se muestran 0 de 0 pendientes", ProgressCaption.Footer(0, 0, 0, store.Texts));
        Assert.Equal("Se muestran 6 de 14 pendientes · 30 % hechas", ProgressCaption.Footer(6, (14, 6), store.Texts));
        // Medio punto se redondea hacia arriba (1 de 8 = 12,5 %).
        Assert.Equal("Se muestran 7 de 7 pendientes · 13 % hechas", ProgressCaption.Footer(7, 7, 1, store.Texts));
    }
}

public class SettingsTests
{
    [Fact]
    public async Task Load_CreatesProvisionalIdentity_OnlyOnce()
    {
        await using var store = await TestStore.CreateAsync(account: "");
        var first = store.Settings.UserId;
        Assert.True(Guid.TryParse(first, out _));
        Assert.Equal(first, store.Settings.LocalUserId);
        Assert.Equal(first, store.Settings.InstallationId);

        // Otra instancia sobre la misma base lee lo guardado, no inventa otro.
        var again = new SettingsService(store.Db);
        await again.LoadAsync();
        await again.LoadAsync();
        Assert.Equal(first, again.UserId);
    }

    [Fact]
    public async Task Load_FillsLocalIdFromExistingUser()
    {
        await using var store = await TestStore.CreateAsync(account: "");
        await store.Settings.SetAsync(SettingsService.KeyUserId, "viejo");
        await store.Db.Connection.ExecuteAsync("DELETE FROM settings WHERE Key = ?", SettingsService.KeyLocalUserId);

        var fresh = new SettingsService(store.Db);
        await fresh.LoadAsync();
        Assert.Equal("viejo", fresh.LocalUserId);
    }

    [Fact]
    public async Task Defaults_AndClamping()
    {
        await using var store = await TestStore.CreateAsync();
        var s = store.Settings;

        Assert.Equal("Yo", s.DisplayName);
        Assert.True(s.SoundEnabled);
        Assert.True(s.HapticsEnabled);
        Assert.True(s.NotificationsEnabled);
        Assert.Equal(9, s.NotifyHour);
        Assert.Equal(0, s.SnoozeMinutes);
        Assert.Equal(TaskFilter.Pending, s.TaskFilter);
        Assert.Null(s.TaskTag);
        Assert.Null(s.FlyoutTag);
        Assert.Equal(string.Empty, s.AccountEmail);
        Assert.Equal(string.Empty, s.AvatarUrl);
        Assert.True(s.IsSupabaseConfigured);

        await s.SetAsync(SettingsService.KeyNotifyHour, "40");
        await s.SetAsync(SettingsService.KeySnoozeMinutes, "9999");
        await s.SetBoolAsync(SettingsService.KeySound, false);
        Assert.Equal(23, s.NotifyHour);
        Assert.Equal(720, s.SnoozeMinutes);
        Assert.False(s.SoundEnabled);

        await s.SetAsync(SettingsService.KeyNotifyHour, "nueve");
        Assert.Equal(9, s.NotifyHour);
        await s.SetAsync(SettingsService.KeySnoozeMinutes, "x");
        Assert.Equal(0, s.SnoozeMinutes);
    }

    [Fact]
    public async Task FilterAndTags_RoundTrip_AndUnknownFilterFallsBack()
    {
        await using var store = await TestStore.CreateAsync();
        var s = store.Settings;

        await s.SetTaskFilterAsync(TaskFilter.Overdue);
        await s.SetTaskTagAsync("casa");
        await s.SetFlyoutTagAsync("oficina");
        Assert.Equal(TaskFilter.Overdue, s.TaskFilter);
        Assert.Equal("casa", s.TaskTag);
        Assert.Equal("oficina", s.FlyoutTag);

        await s.SetTaskTagAsync(null);
        Assert.Null(s.TaskTag);

        await s.SetAsync(SettingsService.KeyTaskFilter, "FiltroDeOtraVersion");
        Assert.Equal(TaskFilters.Default, s.TaskFilter);
        await s.SetAsync(SettingsService.KeyTaskFilter, "999");
        Assert.Equal(TaskFilters.Default, s.TaskFilter);
    }

    [Fact]
    public async Task WhatsNewSeen_IgnoresLeadingZeros()
    {
        await using var store = await TestStore.CreateAsync();
        Assert.True(store.Settings.HasUnseenVersion("2026.9.29.1"));
        await store.Settings.MarkVersionSeenAsync("2026.09.29.01");
        Assert.False(store.Settings.HasUnseenVersion("2026.9.29.1"));
        Assert.True(store.Settings.HasUnseenVersion("2026.9.30.0"));
    }

    [Fact]
    public async Task TokenStore_UsesSettings()
    {
        await using var store = await TestStore.CreateAsync();
        var tokens = new SettingsTokenStore(store.Settings);
        Assert.Null(await tokens.GetAsync("t"));
        await tokens.SetAsync("t", "abc");
        Assert.Equal("abc", await tokens.GetAsync("t"));
        await tokens.SetAsync("t", null);
        Assert.Null(await tokens.GetAsync("t"));
    }
}

public class EntityTests
{
    [Fact]
    public void TaskItem_ProgressAndDerived()
    {
        Assert.Equal(0, new TaskItem().Progress);
        Assert.Equal(1, new TaskItem { IsDone = true }.Progress);
        Assert.Equal(0.25, new TaskItem { StepCount = 4, StepsDone = 1 }.Progress);

        var t = new TaskItem { RecurrenceRule = "weekly:2", Tags = ",a,b," };
        Assert.Equal(RecurrenceKind.Weekly, t.Recurrence.Kind);
        Assert.Equal(["a", "b"], t.TagList);
        Assert.True(new TaskList().IsPrivate);
        Assert.False(new TaskList { GroupId = Guid.NewGuid() }.IsPrivate);
    }

    [Fact]
    public void Attachment_SizeCaption()
    {
        Assert.Equal(string.Empty, new TaskAttachment().SizeCaption);
        Assert.Equal("1 KB", new TaskAttachment { Data = new byte[10] }.SizeCaption);
        Assert.Equal("340 KB", new TaskAttachment { Data = new byte[340 * 1024] }.SizeCaption);
        Assert.Equal($"{1.5:0.#} MB", new TaskAttachment { Data = new byte[3 * 512 * 1024] }.SizeCaption);
        Assert.True(new TaskAttachment().IsUrl);
        Assert.False(new TaskAttachment { Kind = TaskAttachment.KindFile }.IsUrl);
    }
}
