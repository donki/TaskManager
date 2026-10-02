using System.Net;
using System.Text.Json;
using TaskManager.Core.Data;
using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;
using TaskManager.Mobile.Services;
using TaskManager.Mobile.Tests.Infra;
using TaskManager.Tests;

namespace TaskManager.Mobile.Tests;

/// <summary>
/// Lo que deciden los avisos y el trabajo en segundo plano de Android, ya fuera de
/// <c>Platforms/Android</c>: cuando avisar, que decir y que bajar.
/// </summary>
public class BackgroundTests
{
    // -----------------------------------------------------------------------
    // ReminderScheduler
    // -----------------------------------------------------------------------

    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0);

    [Fact]
    public void El_aviso_de_una_tarea_va_a_las_nueve_del_dia_del_plazo()
    {
        var platform = new FakeReminders();
        var scheduler = new ReminderScheduler(platform, () => Now);
        var task = new TaskItem { Title = "Pagar", DueAt = Now.Date.AddDays(1).AddHours(18) };
        var code = ReminderScheduler.RequestCodeFor(task.Id);

        scheduler.ScheduleTaskReminder(task);
        var (alarm, moment) = platform.Scheduled[code];
        Assert.Equal(Now.Date.AddDays(1).AddHours(9), moment);
        Assert.Equal(new ReminderAlarm("Pagar", task.Id), alarm);

        // Hecha, borrada, sin plazo o con el momento ya pasado: se cancela lo que hubiera.
        foreach (var gone in new[]
                 {
                     new TaskItem { Id = task.Id, DueAt = task.DueAt, IsDone = true },
                     new TaskItem { Id = task.Id, DueAt = task.DueAt, Deleted = true },
                     new TaskItem { Id = task.Id },
                     new TaskItem { Id = task.Id, DueAt = Now.Date },
                 })
        {
            scheduler.ScheduleTaskReminder(gone);
            Assert.DoesNotContain(code, platform.Scheduled.Keys);
            scheduler.ScheduleTaskReminder(task);
        }

        scheduler.CancelTaskReminder(task.Id);
        Assert.Empty(platform.Scheduled);
        Assert.Equal(5, platform.Cancelled.Count(c => c == code));
    }

    [Fact]
    public void El_codigo_de_cada_tarea_es_estable_y_no_choca_con_el_diario()
    {
        var id = Guid.NewGuid();
        Assert.Equal(ReminderScheduler.RequestCodeFor(id), ReminderScheduler.RequestCodeFor(id));
        Assert.All(Enumerable.Range(0, 200).Select(_ => ReminderScheduler.RequestCodeFor(Guid.NewGuid())),
            c => Assert.InRange(c, 10, 1_000_009));
    }

    [Fact]
    public void El_resumen_diario_es_hoy_a_esa_hora_o_mañana_si_ya_paso()
    {
        Assert.Equal(Now.Date.AddHours(20), ReminderScheduler.NextDaily(TimeSpan.FromHours(20), Now));
        Assert.Equal(Now.Date.AddDays(1).AddHours(9), ReminderScheduler.NextDaily(TimeSpan.FromHours(9), Now));
        Assert.Equal(Now.Date.AddDays(1).AddHours(12), ReminderScheduler.NextDaily(TimeSpan.FromHours(12), Now));

        var platform = new FakeReminders();
        var scheduler = new ReminderScheduler(platform, () => Now);
        scheduler.ScheduleDailySummary(TimeSpan.FromHours(20));
        Assert.Equal((new ReminderAlarm(DailySummary: true), Now.Date.AddHours(20)), platform.Scheduled[ReminderScheduler.DailyRequestCode]);
        scheduler.CancelDailySummary();
        Assert.Empty(platform.Scheduled);

        // El reloj de verdad por defecto.
        new ReminderScheduler(platform).ScheduleDailySummary(TimeSpan.Zero);
        Assert.True(platform.Scheduled[ReminderScheduler.DailyRequestCode].Moment > DateTime.Now);
    }

    [Fact]
    public async Task Permiso_y_aviso_inmediato_van_al_sistema()
    {
        var platform = new FakeReminders();
        var scheduler = new ReminderScheduler(platform);

        Assert.False(await scheduler.IsAllowedAsync());
        Assert.True(await scheduler.RequestPermissionAsync());
        Assert.True(await scheduler.IsAllowedAsync());

        scheduler.Notify("Mis tareas", "Nueva: pan");
        Assert.Equal(("Nueva: pan".GetHashCode(), "Mis tareas", "Nueva: pan"), platform.Shown.Single());
    }

    // -----------------------------------------------------------------------
    // BackgroundWork
    // -----------------------------------------------------------------------

    private sealed class Db : IAsyncDisposable
    {
        public string Folder { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tm-bg-{Guid.NewGuid():N}");

        public string Path => System.IO.Path.Combine(Folder, BackgroundWork.DatabaseFile);

        public Db() => Directory.CreateDirectory(Folder);

        public async Task<(SettingsService Settings, TaskRepository Repository)> OpenAsync(string account = "cuenta-a")
        {
            var database = new LocalDatabase(Path);
            var settings = new SettingsService(database);
            await settings.LoadAsync();
            await settings.SetAsync(SettingsService.KeyGoogleSub, account);
            await settings.SetAsync(SettingsService.KeyUserId, account);
            await settings.SetAsync(SettingsService.KeyLanguage, "es");
            var repository = new TaskRepository(database, settings);
            await repository.InitializeAsync();
            return (settings, repository);
        }

        public async ValueTask DisposeAsync()
        {
            await new SQLite.SQLiteAsyncConnection(Path).CloseAsync();
            try
            {
                Directory.Delete(Folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task El_resumen_diario_cuenta_lo_pendiente_de_quien_esta_dentro()
    {
        await using var db = new Db();
        Assert.Null(await BackgroundWork.DailySummaryAsync(db.Path));   // sin base todavia

        var (settings, repository) = await db.OpenAsync();
        var texts = new LocalizationService(settings);
        Assert.Null(await BackgroundWork.DailySummaryAsync(db.Path));   // nada pendiente

        var list = await repository.CreateListAsync("Casa");
        await repository.AddTaskAsync(list.Id, "Pan");
        Assert.Equal(new Notice(ReminderScheduler.DailyRequestCode, texts["MenuMyDay"], texts["NotifyOnePending"]),
            await BackgroundWork.DailySummaryAsync(db.Path));

        await repository.AddTaskAsync(list.Id, "Fruta");
        await repository.AddTaskAsync(list.Id, "Leche");
        Assert.Equal(texts.Format("NotifyManyPending", 3), (await BackgroundWork.DailySummaryAsync(db.Path))!.Text);

        // Las de otra cuenta del mismo dispositivo no cuentan.
        await settings.SetAsync(SettingsService.KeyGoogleSub, "cuenta-b");
        Assert.Null(await BackgroundWork.DailySummaryAsync(db.Path));
    }

    [Fact]
    public async Task La_hora_del_diario_al_reprogramar_y_al_encender()
    {
        await using var db = new Db();
        var (settings, _) = await db.OpenAsync();

        Assert.Equal(9, BackgroundWork.RescheduleHour(db.Path));
        Assert.Equal(9, BackgroundWork.BootHour(db.Path));

        await settings.SetAsync(SettingsService.KeyNotifyHour, "21");
        Assert.Equal(21, BackgroundWork.RescheduleHour(db.Path));
        Assert.Equal(21, BackgroundWork.BootHour(db.Path));

        await settings.SetAsync(SettingsService.KeyNotifyHour, "99");
        Assert.Equal(23, BackgroundWork.RescheduleHour(db.Path));
        await settings.SetAsync(SettingsService.KeyNotifyHour, "nueve");
        Assert.Equal(9, BackgroundWork.RescheduleHour(db.Path));

        // Avisos apagados: al encender el movil no se reprograma nada.
        await settings.SetBoolAsync(SettingsService.KeyNotifyEnabled, false);
        Assert.Null(BackgroundWork.BootHour(db.Path));

        // Si la base no se puede abrir: al reprogramar, las nueve; al encender, el fallo sube.
        var nowhere = Path.Combine(db.Folder, "no", "existe", "x.db3");
        Assert.Equal(9, BackgroundWork.RescheduleHour(nowhere));
        Assert.ThrowsAny<Exception>(() => BackgroundWork.BootHour(nowhere));
    }

    private const string RemoteId = "7a1c3d9e-1111-4222-8333-444455556666";

    private static object TaskRow(Guid id, string title, bool done = false) => new
    {
        id,
        list_id = Guid.NewGuid(),
        title,
        notes = "",
        is_pinned = false,
        is_done = done,
        in_progress = false,
        series_id = (Guid?)null,
        done_at = (DateTimeOffset?)null,
        my_day_on = (string?)null,
        due_at = (string?)null,
        planned_for = (string?)null,
        tags = "",
        recurrence_rule = "",
        sort_order = 0,
        updated_at = DateTime.UtcNow,
        deleted = false,
        synced_at = DateTimeOffset.UtcNow,
    };

    private static FakeTokens SignedIn() => new()
    {
        Values =
        {
            ["auth.google_refresh"] = "refresco",
            ["auth.access_token"] = "jwt",
            ["auth.expires_at"] = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
        },
    };

    [Fact]
    public async Task La_pasada_de_fondo_avisa_solo_de_lo_nuevo_que_queda_por_hacer()
    {
        await using var db = new Db();
        var http = new FakeHttp();

        // Sin base no hay nada que bajar.
        Assert.Null(await BackgroundWork.PullArrivalsAsync(db.Path, http.Client(), _ => SignedIn()));

        var (settings, _) = await db.OpenAsync("sub-123");
        await settings.SetAsync(SettingsService.KeyRemoteUserId, RemoteId);
        var texts = new LocalizationService(settings);

        // Sin sesion guardada: nada, y sin preguntar a nadie.
        Assert.Null(await BackgroundWork.PullArrivalsAsync(db.Path, http.Client(), _ => new FakeTokens()));
        Assert.Empty(http.Calls);

        // Una nueva pendiente, una hecha: solo se avisa de la pendiente.
        http.Throw(HttpMethod.Post, "/");
        http.On(HttpMethod.Get, "/rest/v1/", HttpStatusCode.OK, "[]");
        http.On(HttpMethod.Get, "/rest/v1/tasks?", HttpStatusCode.OK, JsonSerializer.Serialize(new[]
        {
            TaskRow(Guid.NewGuid(), "Comprar pan"),
            TaskRow(Guid.NewGuid(), "Ya hecha", done: true),
        }));
        var notice = await BackgroundWork.PullArrivalsAsync(db.Path, http.Client(), _ => SignedIn());
        Assert.Equal(texts.Format("TaskArrivedFromDevice", "Comprar pan"), notice!.Text);
        Assert.Equal(texts["MenuMyTasks"], notice.Title);

        // Varias: se cuentan.
        http.On(HttpMethod.Get, "/rest/v1/tasks?", HttpStatusCode.OK, JsonSerializer.Serialize(new[]
        {
            TaskRow(Guid.NewGuid(), "Uno"),
            TaskRow(Guid.NewGuid(), "Dos"),
        }));
        Assert.Equal(texts.Format("TasksArrivedFromDevice", 2),
            (await BackgroundWork.PullArrivalsAsync(db.Path, http.Client(), _ => SignedIn()))!.Text);

        // Solo hechas, o nada nuevo: no hay aviso.
        http.On(HttpMethod.Get, "/rest/v1/tasks?", HttpStatusCode.OK, JsonSerializer.Serialize(new[] { TaskRow(Guid.NewGuid(), "x", done: true) }));
        Assert.Null(await BackgroundWork.PullArrivalsAsync(db.Path, http.Client(), _ => SignedIn()));
        http.On(HttpMethod.Get, "/rest/v1/tasks?", HttpStatusCode.OK, "[]");
        Assert.Null(await BackgroundWork.PullArrivalsAsync(db.Path, http.Client(), _ => SignedIn()));
    }

    [Fact]
    public async Task En_segundo_plano_no_se_puede_pedir_que_nadie_entre()
    {
        var browser = new BackgroundWork.NoBrowser();
        Assert.StartsWith("http://127.0.0.1", browser.RedirectUri);
        await Assert.ThrowsAsync<InvalidOperationException>(() => browser.AuthenticateAsync(new Uri("https://x")));
    }

    // -----------------------------------------------------------------------
    // Datos de la demostracion
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(false, "es", "Casa")]
    [InlineData(true, "en", "Home")]
    public async Task La_demostracion_siembra_una_vez_en_su_idioma(bool english, string language, string firstList)
    {
        await using var db = new Db();
        var (settings, repository) = await db.OpenAsync("");

        await DemoData.SeedAsync(repository, settings, english);

        Assert.Equal("demo", repository.AccountId);
        Assert.Equal(language, settings.Get(SettingsService.KeyLanguage));
        var lists = await repository.GetPrivateListsAsync();
        Assert.Equal(3, lists.Count);
        Assert.Contains(firstList, lists.Select(l => l.Name));
        var tasks = await repository.GetAllTasksAsync(TaskFilter.All);
        Assert.Equal(10, tasks.Count);
        Assert.Equal(2, tasks.Count(t => t.IsDone));
        Assert.Equal(2, tasks.Count(t => t.IsPinned));
        Assert.Contains(tasks, t => t.Recurrence.Repeats);
        Assert.Equal(6, (await Task.WhenAll(tasks.Select(t => repository.GetStepsAsync(t.Id)))).Sum(s => s.Count));

        // Otra vez: no duplica.
        await DemoData.SeedAsync(repository, settings, english);
        Assert.Equal(10, (await repository.GetAllTasksAsync(TaskFilter.All)).Count);

        // Por defecto, el idioma de la compilacion (español sin -p:DemoLang=en).
        await DemoData.SeedAsync(repository, settings);
        Assert.Equal("es", settings.Get(SettingsService.KeyLanguage));
    }
}
