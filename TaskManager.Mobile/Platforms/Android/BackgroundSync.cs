using Android.App;
using Android.Content;
using TaskManager.Core;
using TaskManager.Core.Data;
using TaskManager.Core.Services;

namespace TaskManager.Mobile.Platforms.Android;

/// <summary>
/// Se baja lo que hayan escrito otros dispositivos <b>con la aplicacion cerrada</b> y avisa de las
/// tareas nuevas.
/// </summary>
/// <remarks>
/// <para><b>Por que no FCM.</b> Lo suyo para esto es una notificacion enviada desde un servidor,
/// pero eso exige un proyecto de Firebase y su <c>google-services.json</c>, que solo se crea desde
/// la consola de Google del dueño de la aplicacion. Mientras no exista, esto cumple lo mismo desde
/// el propio movil: una alarma periodica que se baja los cambios y avisa. La diferencia practica es
/// el <b>retardo</b> —hasta media hora en vez de segundos— y que el sistema puede espaciarla mas si
/// el movil lleva mucho parado.</para>
///
/// <para><b>Alarma inexacta, a proposito.</b> <c>setWindow</c> deja que Android agrupe este aviso
/// con otros y no despierte el aparato solo para esto. La alarma exacta es un permiso restringido
/// que Google reserva para alarmas y temporizadores de verdad, y esto no lo es.</para>
///
/// <para>Se ejecuta sin nada de MAUI levantado: monta a mano lo que necesita sobre la misma base de
/// datos, igual que <c>ReminderReceiver</c>.</para>
/// </remarks>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class BackgroundSyncReceiver : BroadcastReceiver
{
    private const int RequestCode = 7;

    /// <summary>Cada cuanto se mira. Ni tan seguido que gaste bateria ni tan poco que no sirva.</summary>
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Deja programada la siguiente pasada. Se llama al arrancar la aplicacion y al encender el
    /// movil; cada pasada vuelve a programarse, que es como se consigue que sea periodica sin usar
    /// alarmas repetitivas exactas.
    /// </summary>
    public static void Schedule()
    {
        var context = global::Android.App.Application.Context;
        var manager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (manager is null)
        {
            return;
        }

        var intent = new Intent(context, typeof(BackgroundSyncReceiver));
        var pending = PendingIntent.GetBroadcast(context, RequestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        if (pending is null)
        {
            return;
        }

        var moment = DateTime.UtcNow.Add(Every);
        var triggerAt = (long)(moment - DateTime.UnixEpoch).TotalMilliseconds;

        manager.SetWindow(AlarmType.RtcWakeup, triggerAt, 15 * 60 * 1000, pending);
    }

    public override async void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        // La bajada tarda: sin esto Android puede matar el proceso a mitad de camino.
        var pending = GoAsync();

        try
        {
            await SyncAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("TaskManager", $"Background sync failed: {ex.Message}");
        }
        finally
        {
            // Se reprograma pase lo que pase: si una pasada falla, la cadena no puede cortarse.
            Schedule();
            pending.Finish();
        }
    }

    private static async Task SyncAsync(Context context)
    {
        if (!SupabaseConfig.IsConfigured)
        {
            return;
        }

        var path = Path.Combine(context.FilesDir?.AbsolutePath ?? string.Empty, "taskmanager.db3");
        if (!File.Exists(path))
        {
            return;   // Todavia no se ha abierto la aplicacion ni una vez.
        }

        var database = new LocalDatabase(path);
        var repository = new TaskRepository(database);
        await repository.InitializeAsync().ConfigureAwait(false);

        var settings = new SettingsService(database);
        await settings.LoadAsync().ConfigureAwait(false);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        var tokens = new Services.SecureTokenStore(new SettingsTokenStore(settings));
        var auth = new SupabaseAuthService(http, settings, tokens, new NoBrowser());

        // Sin sesion guardada no hay nada que bajar, y aqui no se puede pedir que entre nadie.
        if (await auth.RestoreSessionAsync().ConfigureAwait(false) is null)
        {
            return;
        }

        var sync = new SupabaseSyncService(http, repository, settings, auth);

        var arrived = new List<Guid>();
        sync.RemoteChanged += (_, change) =>
        {
            if (change.Entity == "tasks" && Guid.TryParse(change.EntityId, out var id))
            {
                arrived.Add(id);
            }
        };

        await sync.StartAsync().ConfigureAwait(false);

        if (arrived.Count == 0)
        {
            return;
        }

        // Solo se avisa de lo que de verdad queda por hacer: una tarea que llega ya terminada, o
        // borrada, no es una noticia que merezca despertar a nadie.
        var texts = new LocalizationService(settings);
        var pendingTasks = new List<string>();

        foreach (var id in arrived.Distinct())
        {
            var task = await repository.GetTaskAsync(id).ConfigureAwait(false);
            if (task is not null && !task.Deleted && !task.IsDone)
            {
                pendingTasks.Add(task.Title);
            }
        }

        if (pendingTasks.Count == 0)
        {
            return;
        }

        var message = pendingTasks.Count == 1
            ? texts.Format("TaskArrivedFromDevice", pendingTasks[0])
            : texts.Format("TasksArrivedFromDevice", pendingTasks.Count);

        ReminderReceiver.ShowNow(context, message.GetHashCode(), texts["MenuMyTasks"], message);
    }

    /// <summary>
    /// No hay navegador aqui: en segundo plano no se puede pedir que nadie entre. Solo existe para
    /// poder construir el servicio de sesion, que en este camino unicamente <b>renueva</b>.
    /// </summary>
    private sealed class NoBrowser : IOAuthBrowser
    {
        public string RedirectUri => "http://127.0.0.1:0/auth/";

        public Task<Uri> AuthenticateAsync(Uri authorizeUrl, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No se puede entrar desde el segundo plano.");
    }
}
