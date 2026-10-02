using Android.App;
using Android.Content;
using TaskManager.Core.Services;
using TaskManager.Mobile.Services;

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
/// <para>Se ejecuta sin nada de MAUI levantado: lo que hace lo monta a mano
/// <see cref="BackgroundWork.PullArrivalsAsync"/> sobre la misma base de datos.</para>
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
        var pending = PendingIntent.GetBroadcast(context, RequestCode, new Intent(context, typeof(BackgroundSyncReceiver)),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        if (pending is not null && context.GetSystemService(Context.AlarmService) is AlarmManager manager)
        {
            manager.SetWindow(AlarmType.RtcWakeup,
                (long)(DateTime.UtcNow.Add(Every) - DateTime.UnixEpoch).TotalMilliseconds, 15 * 60 * 1000, pending);
        }
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
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            if (await BackgroundWork.PullArrivalsAsync(ReminderReceiver.DatabasePath(context), http,
                    settings => new SecureTokenStore(new SettingsTokenStore(settings))).ConfigureAwait(false) is { } notice)
            {
                ReminderReceiver.Show(context, notice);
            }
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
}
