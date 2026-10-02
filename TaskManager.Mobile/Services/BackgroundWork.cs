using TaskManager.Core;
using TaskManager.Core.Data;
using TaskManager.Core.Services;

namespace TaskManager.Mobile.Services;

/// <summary>Un aviso ya redactado: titulo, texto y el id con el que se enseña.</summary>
public sealed record Notice(int Id, string Title, string Text);

/// <summary>
/// Lo que hacen los receptores de Android en segundo plano: el resumen diario, reprogramar al
/// encender el movil y la pasada de sincronizacion con la aplicacion cerrada.
/// </summary>
/// <remarks>
/// <para>Puede ejecutarse con la aplicacion cerrada, asi que no da por hecho que exista nada de
/// MAUI: abre la base de datos por su ruta y monta a mano lo que necesita. Los receptores
/// (<c>Platforms/Android</c>) solo traducen el <c>Intent</c> y enseñan el resultado; lo que se
/// decide esta aqui, donde se prueba fuera del movil.</para>
/// </remarks>
public static class BackgroundWork
{
    /// <summary>Nombre de la base de datos de la aplicacion, dentro de su carpeta de ficheros.</summary>
    public const string DatabaseFile = "taskmanager.db3";

    /// <summary>
    /// Un solo aviso con lo que queda por hacer. Null si no hay base todavia o no queda nada.
    /// </summary>
    public static async Task<Notice?> DailySummaryAsync(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return null;
        }

        // No hay contenedor del que sacar los servicios: se construyen aqui sobre la misma base. Los
        // ajustes van primero porque de ahi sale la cuenta que esta dentro, y solo se cuentan sus
        // tareas: con dos cuentas en el dispositivo, contarlas todas avisaria de pendientes que no
        // son de quien mira.
        var (settings, repository) = await OpenAsync(databasePath);

        // Todo lo que queda por hacer, no solo lo de «Mi Dia»: esa pantalla ya no existe, asi que
        // contar por ella daba siempre cero y el recordatorio diario no saltaba nunca.
        var pending = await repository.CountPendingAsync();
        if (pending == 0)
        {
            return null;
        }

        var texts = new LocalizationService(settings);
        var text = pending == 1 ? texts["NotifyOnePending"] : texts.Format("NotifyManyPending", pending);
        return new Notice(ReminderScheduler.DailyRequestCode, texts["MenuMyDay"], text);
    }

    /// <summary>
    /// La hora (0-23) a la que se vuelve a poner el resumen diario al dispararse. Si el ajuste no se
    /// puede leer, las nueve: mejor un aviso a las nueve que quedarse sin recordatorio.
    /// </summary>
    public static int RescheduleHour(string databasePath)
    {
        try
        {
            return ReadHour(LoadSettings(databasePath));
        }
        catch (Exception)
        {
            return 9;
        }
    }

    /// <summary>
    /// Al encender el movil: la hora del resumen diario, o null si los avisos estan apagados (y
    /// entonces no se reprograma nada). Un fallo al leer se deja subir: lo apunta el receptor.
    /// </summary>
    public static int? BootHour(string databasePath)
    {
        var settings = LoadSettings(databasePath);
        return settings.GetBool("notify.enabled", true) ? ReadHour(settings) : null;
    }

    private static SettingsService LoadSettings(string databasePath)
    {
        var settings = new SettingsService(new LocalDatabase(databasePath));
        settings.LoadAsync().GetAwaiter().GetResult();
        return settings;
    }

    private static int ReadHour(SettingsService settings) =>
        Math.Clamp(int.TryParse(settings.Get("notify.hour", "9"), out var parsed) ? parsed : 9, 0, 23);

    private static async Task<(SettingsService Settings, TaskRepository Repository)> OpenAsync(string databasePath)
    {
        var database = new LocalDatabase(databasePath);

        var settings = new SettingsService(database);
        await settings.LoadAsync().ConfigureAwait(false);

        var repository = new TaskRepository(database, settings);
        await repository.InitializeAsync().ConfigureAwait(false);
        return (settings, repository);
    }

    /// <summary>
    /// Se baja lo que hayan escrito otros dispositivos y redacta el aviso de las tareas nuevas que
    /// quedan por hacer. Null si no hay nada que avisar (o no hay base, ni sesion, ni servidor).
    /// </summary>
    /// <param name="tokens">El almacen de la sesion, montado sobre los ajustes de esa base.</param>
    public static async Task<Notice?> PullArrivalsAsync(
        string databasePath, HttpClient http, Func<SettingsService, ITokenStore> tokens)
    {
        if (!SupabaseConfig.IsConfigured || !File.Exists(databasePath))
        {
            return null;   // Todavia no se ha abierto la aplicacion ni una vez.
        }

        // Primero los ajustes: de ahi sale la cuenta que esta dentro, que es de quien se sube y se
        // baja. Sin ella el repositorio no sabria de quien es ninguna fila.
        var (settings, repository) = await OpenAsync(databasePath).ConfigureAwait(false);

        var auth = new SupabaseAuthService(http, settings, tokens(settings), new NoBrowser());

        // Sin sesion guardada no hay nada que bajar, y aqui no se puede pedir que entre nadie.
        if (await auth.RestoreSessionAsync().ConfigureAwait(false) is null)
        {
            return null;
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

        // Solo se avisa de lo que de verdad queda por hacer: una tarea que llega ya terminada, o
        // borrada, no es una noticia que merezca despertar a nadie.
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
            return null;
        }

        var texts = new LocalizationService(settings);
        var message = pendingTasks.Count == 1
            ? texts.Format("TaskArrivedFromDevice", pendingTasks[0])
            : texts.Format("TasksArrivedFromDevice", pendingTasks.Count);

        return new Notice(message.GetHashCode(), texts["MenuMyTasks"], message);
    }

    /// <summary>
    /// No hay navegador aqui: en segundo plano no se puede pedir que nadie entre. Solo existe para
    /// poder construir el servicio de sesion, que en este camino unicamente <b>renueva</b>.
    /// </summary>
    internal sealed class NoBrowser : IOAuthBrowser
    {
        public string RedirectUri => "http://127.0.0.1:0/auth/";

        public Task<Uri> AuthenticateAsync(Uri authorizeUrl, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No se puede entrar desde el segundo plano.");
    }
}
