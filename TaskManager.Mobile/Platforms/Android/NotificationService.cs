using Android.App;
using Android.Content;
using AndroidX.Core.App;
using TaskManager.Mobile.Services;

namespace TaskManager.Mobile.Platforms.Android;

/// <summary>
/// Lo nativo de los avisos en Android: <c>AlarmManager</c> con un receptor propio y
/// <c>NotificationManagerCompat</c>, sin dependencias externas. Que avisar y cuando lo decide
/// <see cref="ReminderScheduler"/>.
/// </summary>
public sealed class AndroidReminderPlatform : IReminderPlatform
{
    public async Task<bool> IsAllowedAsync() =>
        !OperatingSystem.IsAndroidVersionAtLeast(33)
        || await Permissions.CheckStatusAsync<Permissions.PostNotifications>() == PermissionStatus.Granted;

    public async Task<bool> RequestPermissionAsync() =>
        !OperatingSystem.IsAndroidVersionAtLeast(33)
        || await Permissions.RequestAsync<Permissions.PostNotifications>() == PermissionStatus.Granted;

    public void Schedule(int requestCode, ReminderAlarm alarm, DateTime moment)
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(ReminderReceiver));
        intent.PutExtra(ReminderReceiver.ExtraTitle, alarm.Title);
        intent.PutExtra(ReminderReceiver.ExtraTaskId, alarm.TaskId?.ToString());
        intent.PutExtra(ReminderReceiver.ExtraDailySummary, alarm.DailySummary);

        // setWindow: el sistema elige el momento dentro de una ventana de 15 minutos, que es lo que
        // permite agrupar alarmas y no despertar el movil solo para esto.
        var pending = PendingIntent.GetBroadcast(context, requestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        if (pending is not null && context.GetSystemService(Context.AlarmService) is AlarmManager manager)
        {
            manager.SetWindow(AlarmType.RtcWakeup,
                (long)(moment.ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds, 15 * 60 * 1000, pending);
        }
    }

    public void Cancel(int requestCode)
    {
        var context = global::Android.App.Application.Context;
        var pending = PendingIntent.GetBroadcast(context, requestCode, new Intent(context, typeof(ReminderReceiver)),
            PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
        if (pending is not null)
        {
            (context.GetSystemService(Context.AlarmService) as AlarmManager)?.Cancel(pending);
            pending.Cancel();
        }
    }

    public void Show(int id, string title, string text) =>
        ReminderReceiver.Show(global::Android.App.Application.Context, new Notice(id, title, text));
}

/// <summary>
/// Recibe la alarma y muestra el aviso. Puede ejecutarse con la aplicacion cerrada: lo que hay que
/// leer de la base lo hace <see cref="BackgroundWork"/>.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class ReminderReceiver : BroadcastReceiver
{
    public const string ExtraTitle = "title";
    public const string ExtraTaskId = "taskId";
    public const string ExtraDailySummary = "daily";

    private const string ChannelId = "taskmanager_reminders";

    internal static string DatabasePath(Context context) =>
        Path.Combine(context.FilesDir?.AbsolutePath ?? string.Empty, BackgroundWork.DatabaseFile);

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
            if (!intent.GetBooleanExtra(ExtraDailySummary, false))
            {
                var title = intent.GetStringExtra(ExtraTitle) ?? "Task Manager";
                Show(context, new Notice(title.GetHashCode(), "Task Manager", title));
                return;
            }

            if (await BackgroundWork.DailySummaryAsync(DatabasePath(context)) is { } notice)
            {
                Show(context, notice);
            }

            // La alarma diaria no se repite sola: al dispararse se vuelve a poner para mañana.
            new ReminderScheduler(new AndroidReminderPlatform())
                .ScheduleDailySummary(TimeSpan.FromHours(BackgroundWork.RescheduleHour(DatabasePath(context))));
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

    /// <summary>Enseña el aviso, con el canal creado. Al tocarlo se abre la aplicacion.</summary>
    internal static void Show(Context context, Notice notice)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            (context.GetSystemService(Context.NotificationService) as NotificationManager)?.CreateNotificationChannel(
                new NotificationChannel(ChannelId, "Recordatorios", NotificationImportance.Default));
        }

        var launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle(notice.Title)
            .SetContentText(notice.Text)
            // Icono de marca, no el generico del sistema (constitucion Mobile 7).
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetAutoCancel(true)
            .SetContentIntent(launch is null ? null : PendingIntent.GetActivity(context, 0, launch,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable))
            .SetPriority((int)NotificationPriority.Default)
            .Build();

        try
        {
            NotificationManagerCompat.From(context).Notify(Math.Abs(notice.Id), notification);
        }
        catch (Java.Lang.SecurityException)
        {
            // Permiso de notificaciones revocado entre medias: no hay nada que hacer ni que romper.
        }
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
        try
        {
            if (context is not null && BackgroundWork.BootHour(ReminderReceiver.DatabasePath(context)) is { } hour)
            {
                new ReminderScheduler(new AndroidReminderPlatform()).ScheduleDailySummary(TimeSpan.FromHours(hour));

                // Al reiniciar se pierden todas las alarmas, tambien la de la pasada de fondo.
                BackgroundSyncReceiver.Schedule();
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("TaskManager", $"Boot reschedule failed: {ex.Message}");
        }
    }
}
