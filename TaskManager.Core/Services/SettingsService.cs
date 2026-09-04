using TaskManager.Core.Data;
using TaskManager.Core.Models;

namespace TaskManager.Core.Services;

/// <summary>
/// Ajustes locales en la propia base de datos, con las claves compartidas por movil y escritorio.
/// La clave compartida de un grupo NUNCA se guarda aqui: se canjea una vez y lo que queda es la
/// pertenencia (ARQUITECTURA.md seccion 4).
/// </summary>
public sealed class SettingsService
{
    public const string KeyUserId = "user.id";
    public const string KeyLocalUserId = "user.local_id";
    /// <summary>
    /// La identidad de verdad, la misma en todos los aparatos: el identificador de la cuenta que da
    /// el proveedor (el <c>sub</c> de Google o el <c>oid</c> de Microsoft).
    /// </summary>
    /// <remarks>
    /// El nombre guardado sigue siendo <c>user.google_sub</c> aunque ya valga tambien para
    /// Microsoft. <b>Cambiarlo cerraria la sesion de todo el mundo al actualizar</b>: la aplicacion
    /// buscaria una clave que en su base no existe, no encontraria identidad y volveria a pedir la
    /// entrada. Un nombre algo viejo cuesta menos que eso.
    /// </remarks>
    public const string KeyGoogleSub = "user.google_sub";

    /// <summary>Con que se entro: <c>Google</c> o <c>Microsoft</c>. Hace falta para renovar.</summary>
    public const string KeyAuthProvider = "user.auth_provider";
    /// <summary>El <c>auth.uid()</c> de Supabase, que es distinto y solo vale para las filas que suben.</summary>
    public const string KeyRemoteUserId = "user.remote_id";
    public const string KeyAccountEmail = "user.email";
    public const string KeyAvatarUrl = "user.avatar";
    public const string KeyDisplayName = "user.display_name";
    /// <summary>Idioma elegido (es/en). Vacio = seguir al del sistema.</summary>
    public const string KeyLanguage = "user.language";
    public const string KeyLlmEndpoint = "ai.endpoint";
    public const string KeyLlmModel = "ai.model";
    public const string KeySound = "celebration.sound";
    public const string KeyHaptics = "celebration.haptics";
    public const string KeyNotifyEnabled = "notify.enabled";
    public const string KeyNotifyHour = "notify.hour";

    /// <summary>
    /// Cada cuantos minutos se repite el aviso de tareas pendientes. <b>Cero = no repetir</b>, que
    /// es lo de siempre: un solo aviso al dia.
    /// </summary>
    public const string KeySnoozeMinutes = "notify.snooze";
    public const string KeyHotkey = "desktop.hotkey";
    public const string KeyStartWithWindows = "desktop.autostart";

    /// <summary>
    /// El filtro de «Mis tareas» que estaba puesto la ultima vez, y su etiqueta.
    /// </summary>
    /// <remarks>
    /// <para>Se guardan porque el filtro <b>es una forma de trabajar</b>, no una consulta suelta:
    /// quien mira «caducadas» o «#Casa» lo vuelve a mirar al abrir, y sin esto habia que ponerlo
    /// otra vez cada vez. Vale igual en Windows y en Android, con las mismas claves, aunque cada
    /// aparato tenga las suyas: no viajan al servidor.</para>
    ///
    /// <para>El buscador NO se guarda a proposito: reabrir la aplicacion con un texto de ayer
    /// escondiendo casi todo se lee como que las tareas han desaparecido.</para>
    /// </remarks>
    public const string KeyTaskFilter = "filter.tasks";
    public const string KeyTaskTag = "filter.tasks_tag";

    /// <summary>La etiqueta del panel rapido, que tiene su propia fila de etiquetas.</summary>
    public const string KeyFlyoutTag = "filter.flyout_tag";

    private readonly LocalDatabase _db;
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private bool _loaded;

    public SettingsService(LocalDatabase db) => _db = db;

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        await _db.InitializeAsync().ConfigureAwait(false);
        var entries = await _db.Connection.Table<SettingEntry>().ToListAsync().ConfigureAwait(false);
        foreach (var entry in entries)
        {
            _cache[entry.Key] = entry.Value;
        }

        // Identidad provisional mientras no se entra con Google: sin ella no se puede atribuir XP
        // ni autoria. Se guarda aparte para poder traspasar lo hecho en local al entrar (ver
        // TaskRepository.ReassignUserAsync).
        if (!_cache.ContainsKey(KeyUserId))
        {
            var local = Guid.NewGuid().ToString();
            await SetAsync(KeyUserId, local).ConfigureAwait(false);
            await SetAsync(KeyLocalUserId, local).ConfigureAwait(false);
        }
        else if (!_cache.ContainsKey(KeyLocalUserId))
        {
            await SetAsync(KeyLocalUserId, Get(KeyUserId)).ConfigureAwait(false);
        }

        _loaded = true;
    }

    public string Get(string key, string fallback = "") =>
        _cache.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;

    public bool GetBool(string key, bool fallback) =>
        _cache.TryGetValue(key, out var value) ? value == "1" : fallback;

    public async Task SetAsync(string key, string value)
    {
        _cache[key] = value;
        await _db.Connection.InsertOrReplaceAsync(new SettingEntry { Key = key, Value = value })
                 .ConfigureAwait(false);
    }

    public Task SetBoolAsync(string key, bool value) => SetAsync(key, value ? "1" : "0");

    public string UserId => Get(KeyUserId);

    public string DisplayName => Get(KeyDisplayName, "Yo");

    public string LlmEndpoint => Get(KeyLlmEndpoint, "http://localhost:11434");

    public string LlmModel => Get(KeyLlmModel, "qwen2.5:3b-instruct");

    public bool SoundEnabled => GetBool(KeySound, true);

    public bool HapticsEnabled => GetBool(KeyHaptics, true);

    /// <summary>Recordatorio diario de lo que queda pendiente. Encendido por defecto.</summary>
    public bool NotificationsEnabled => GetBool(KeyNotifyEnabled, true);

    /// <summary>
    /// Cada cuanto se repite el aviso de pendientes, en minutos. Cero significa no repetir.
    /// </summary>
    public int SnoozeMinutes => int.TryParse(Get(KeySnoozeMinutes, "0"), out var m) ? Math.Clamp(m, 0, 720) : 0;

    /// <summary>Hora del recordatorio diario (0-23).</summary>
    public int NotifyHour => int.TryParse(Get(KeyNotifyHour, "9"), out var hour) ? Math.Clamp(hour, 0, 23) : 9;

    public string AccountEmail => Get(KeyAccountEmail);

    public string AvatarUrl => Get(KeyAvatarUrl);

    /// <summary>Identificador provisional anterior a la entrada con Google.</summary>
    public string LocalUserId => Get(KeyLocalUserId, UserId);

    /// <summary>
    /// Identificador de esta instalacion: el GUID que se genero la primera vez que se abrio la
    /// aplicacion. Mientras no haya cuenta, ES la identidad del usuario (ver <see cref="AuthOptions"/>).
    /// Se pierde si se desinstala la aplicacion o se borran sus datos.
    /// </summary>
    public string InstallationId => LocalUserId;

    /// <summary>
    /// El backend viene incrustado (<see cref="SupabaseConfig"/>): una sola base para todos los
    /// grupos y nada que rellenar. Se conserva la propiedad porque la interfaz pregunta si hay
    /// servidor antes de ofrecer entrar o unirse a un grupo.
    /// </summary>
    public bool IsSupabaseConfigured => SupabaseConfig.IsConfigured;

    // -----------------------------------------------------------------------
    // Lo ultimo que se estaba mirando
    // -----------------------------------------------------------------------

    /// <summary>
    /// El filtro con el que se dejo «Mis tareas». Si lo guardado no se entiende —una version
    /// anterior, o un filtro que ya no existe— se vuelve al de partida en vez de fallar.
    /// </summary>
    public TaskFilter TaskFilter =>
        Enum.TryParse<TaskFilter>(Get(KeyTaskFilter), out var filter) && Enum.IsDefined(filter)
            ? filter
            : TaskFilters.Default;

    public Task SetTaskFilterAsync(TaskFilter filter) => SetAsync(KeyTaskFilter, filter.ToString());

    /// <summary>La etiqueta que estaba puesta, o <c>null</c> si no habia ninguna.</summary>
    public string? TaskTag => Tag(KeyTaskTag);

    public Task SetTaskTagAsync(string? tag) => SetAsync(KeyTaskTag, tag ?? string.Empty);

    /// <summary>La etiqueta del panel rapido, aparte de la de «Mis tareas».</summary>
    public string? FlyoutTag => Tag(KeyFlyoutTag);

    public Task SetFlyoutTagAsync(string? tag) => SetAsync(KeyFlyoutTag, tag ?? string.Empty);

    /// <summary>
    /// Una etiqueta guardada. Vacio significa «sin filtro», que es distinto de la etiqueta
    /// <see cref="Data.TaskRepository.NoTag"/> —«las que no llevan ninguna»—, y por eso se guarda
    /// como cadena vacia y no como texto.
    /// </summary>
    private string? Tag(string key)
    {
        var tag = Get(key);
        return tag.Length == 0 ? null : tag;
    }
}

/// <summary>
/// Almacen de tokens de reserva: la propia tabla de ajustes. Va **en claro**, asi que solo se usa
/// donde no hay nada mejor; Android usa SecureStorage y Windows DPAPI.
/// </summary>
public sealed class SettingsTokenStore : ITokenStore
{
    private readonly SettingsService _settings;

    public SettingsTokenStore(SettingsService settings) => _settings = settings;

    public Task<string?> GetAsync(string key)
    {
        var value = _settings.Get(key);
        return Task.FromResult<string?>(value.Length == 0 ? null : value);
    }

    public Task SetAsync(string key, string? value) => _settings.SetAsync(key, value ?? string.Empty);
}
