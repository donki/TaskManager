using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using TaskManager.Core.Models;
using TaskManager.Core.Services;

namespace TaskManager.Mobile.Platforms.Android;

/// <inheritdoc cref="INotificationService"/>
/// <remarks>
/// Con <c>AlarmManager</c> y un receptor propio, sin dependencias externas. Los avisos son
/// **inexactos** a proposito: la alarma exacta es un permiso restringido en Android 12+ que Google
/// reserva para alarmas y temporizadores de verdad, y un recordatorio de tareas no lo es. Un margen
/// de unos minutos no le importa a nadie y evita pedir un permiso que no toca.
/// </remarks>
public sealed class NotificationService : INotificationService
{
    /// <summary>Id fijo del recordatorio diario; los de tarea usan el hash de su identificador.</summary>
    private const int DailyRequestCode = 1;

    public async Task<bool> IsAllowedAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return true;
        }

        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        return status == PermissionStatus.Granted;
    }

    public async Task<bool> RequestPermissionAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return true;
        }

        var status = await Permissions.RequestAsync<Permissions.PostNotifications>();
        return status == PermissionStatus.Granted;
    }

    public void ScheduleTaskReminder(TaskItem task)
    {
        var code = RequestCodeFor(task.Id);

        // Sin plazo o ya hecha: lo que hubiera programado sobra.
        if (task.IsDone || task.Deleted || task.DueAt is not { } due)
        {
            Cancel(code);
            return;
        }

        // El aviso va a las 9:00 del dia del plazo, no a medianoche, que es cuando nadie lo lee.
        var moment = due.Date.AddHours(9);
        if (moment <= DateTime.Now)
        {
            Cancel(code);
            return;
        }

        var intent = new Intent(global::Android.App.Application.Context, typeof(ReminderReceiver));
        intent.PutExtra(ReminderReceiver.ExtraTitle, task.Title);
        intent.PutExtra(ReminderReceiver.ExtraTaskId, task.Id.ToString());

        Schedule(code, intent, moment);
    }

    public void CancelTaskReminder(Guid taskId) => Cancel(RequestCodeFor(taskId));

    public void ScheduleDailySummary(TimeSpan timeOfDay)
    {
        var intent = new Intent(global::Android.App.Application.Context, typeof(ReminderReceiver));
        intent.PutExtra(ReminderReceiver.ExtraDailySummary, true);

        var next = DateTime.Today.Add(timeOfDay);
        if (next <= DateTime.Now)
        {
            next = next.AddDays(1);
        }

        Schedule(DailyRequestCode, intent, next);
    }

    public void CancelDailySummary() => Cancel(DailyRequestCode);

    // ==================================================================================

    private static void Schedule(int requestCode, Intent intent, DateTime moment)
    {
        var context = global::Android.App.Application.Context;
        var manager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (manager is null)
        {
            return;
        }

        var pending = PendingIntent.GetBroadcast(context, requestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        if (pending is null)
        {
            return;
        }

        var triggerAt = (long)(moment.ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds;

        // setWindow: el sistema elige el momento dentro de una ventana de 15 minutos, que es lo que
        // permite agrupar alarmas y no despertar el movil solo para esto.
        manager.SetWindow(AlarmType.RtcWakeup, triggerAt, 15 * 60 * 1000, pending);
    }

    private static void Cancel(int requestCode)
    {
        var context = global::Android.App.Application.Context;
        var manager = (AlarmManager?)context.GetSystemService(Context.AlarmService);

        var intent = new Intent(context, typeof(ReminderReceiver));
        var pending = PendingIntent.GetBroadcast(context, requestCode, intent,
            PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);

        if (pending is not null)
        {
            manager?.Cancel(pending);
            pending.Cancel();
        }
    }

    /// <summary>Codigo estable por tarea: el mismo identificador siempre reprograma su aviso.</summary>
    private static int RequestCodeFor(Guid id) => Math.Abs(id.GetHashCode()) % 1_000_000 + 10;
}

/// <summary>
/// Recibe la alarma y muestra el aviso. Puede ejecutarse con la aplicacion cerrada, asi que no da
/// por hecho que exista nada de MAUI: abre la base de datos por su ruta y consulta lo que necesita.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class ReminderReceiver : BroadcastReceiver
{
    public const string ExtraTitle = "title";
    public const string ExtraTaskId = "taskId";
    public const string ExtraDailySummary = "daily";

    private const string ChannelId = "taskmanager_reminders";

    public override async void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        // El aviso puede tardar (hay que leer la base): sin esto, Android puede matar el proceso a
        // mitad de camino.
        var pending = GoAsync();

        try
        {
            CreateChannel(context);

            if (intent.GetBooleanExtra(ExtraDailySummary, false))
            {
                await ShowDailySummaryAsync(context);
                Reschedule(context);
                return;
            }

            var title = intent.GetStringExtra(ExtraTitle) ?? "Task Manager";
            Show(context, title.GetHashCode(), "Task Manager", title);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("TaskManager", $"Reminder failed: {ex.Message}");
        }
        finally
        {
            pending.Finish();
        }
    }

    /// <summary>
    /// Un solo aviso con lo que queda por hacer hoy. Se lee la base directamente porque el proceso
    /// puede no tener la aplicacion levantada.
    /// </summary>
    private static async Task ShowDailySummaryAsync(Context context)
    {
        var path = Path.Combine(context.FilesDir?.AbsolutePath ?? string.Empty, "taskmanager.db3");
        if (!File.Exists(path))
        {
            return;
        }

        var database = new TaskManager.Core.Data.LocalDatabase(path);
        var repository = new TaskManager.Core.Data.TaskRepository(database);
        await repository.InitializeAsync();

        var pending = await repository.CountMyDayPendingAsync();
        if (pending == 0)
        {
            return;
        }

        // El aviso puede saltar con la aplicacion cerrada, asi que no hay contenedor del que sacar
        // el servicio: se construye aqui sobre la misma base de datos.
        var settings = new TaskManager.Core.Services.SettingsService(database);
        await settings.LoadAsync();
        var texts = new TaskManager.Core.Services.LocalizationService(settings);

        var text = pending == 1 ? texts["NotifyOnePending"] : texts.Format("NotifyManyPending", pending);
        Show(context, 1, texts["MenuMyDay"], text);
    }

    /// <summary>La alarma diaria no se repite sola: al dispararse se vuelve a poner para mañana.</summary>
    private static void Reschedule(Context context)
    {
        var database = new TaskManager.Core.Data.LocalDatabase(
            Path.Combine(context.FilesDir?.AbsolutePath ?? string.Empty, "taskmanager.db3"));

        var settings = new TaskManager.Core.Services.SettingsService(database);
        var hour = 9;

        try
        {
            settings.LoadAsync().GetAwaiter().GetResult();
            hour = int.TryParse(settings.Get("notify.hour", "9"), out var parsed) ? parsed : 9;
        }
        catch (Exception)
        {
            // Si no se puede leer el ajuste, se reprograma a la hora por defecto: mejor un aviso a
            // las nueve que quedarse sin recordatorio.
        }

        new NotificationService().ScheduleDailySummary(TimeSpan.FromHours(Math.Clamp(hour, 0, 23)));
    }

    private static void Show(Context context, int id, string title, string text)
    {
        var launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        var pending = launch is null
            ? null
            : PendingIntent.GetActivity(context, 0, launch,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle(title)
            .SetContentText(text)
            // Icono de marca, no el generico del sistema (constitucion Mobile 7).
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetAutoCancel(true)
            .SetContentIntent(pending)
            .SetPriority((int)NotificationPriority.Default)
            .Build();

        var manager = NotificationManagerCompat.From(context);
        try
        {
            manager.Notify(Math.Abs(id), notification);
        }
        catch (Java.Lang.SecurityException)
        {
            // Permiso de notificaciones revocado entre medias: no hay nada que hacer ni que romper.
        }
    }

    private static void CreateChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        var channel = new NotificationChannel(ChannelId, "Recordatorios", NotificationImportance.Default);
        manager?.CreateNotificationChannel(channel);
    }
}

/// <summary>
/// Al reiniciar el movil se pierden las alarmas programadas: hay que volver a ponerlas o el
/// recordatorio diario desaparece sin que nadie se entere.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted])]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        var database = new TaskManager.Core.Data.LocalDatabase(
            Path.Combine(context.FilesDir?.AbsolutePath ?? string.Empty, "taskmanager.db3"));

        var settings = new TaskManager.Core.Services.SettingsService(database);

        try
        {
            settings.LoadAsync().GetAwaiter().GetResult();
            if (!settings.GetBool("notify.enabled", true))
            {
                return;
            }

            var hour = int.TryParse(settings.Get("notify.hour", "9"), out var parsed) ? parsed : 9;
            new NotificationService().ScheduleDailySummary(TimeSpan.FromHours(Math.Clamp(hour, 0, 23)));
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("TaskManager", $"Boot reschedule failed: {ex.Message}");
        }
    }
}
