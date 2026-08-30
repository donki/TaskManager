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
    public const string KeyAccountEmail = "user.email";
    public const string KeyAvatarUrl = "user.avatar";
    /// <summary>El usuario eligio seguir sin cuenta: no se le vuelve a preguntar al arrancar.</summary>
    public const string KeyAuthSkipped = "auth.skipped";
    public const string KeyDisplayName = "user.display_name";
    /// <summary>Idioma elegido (es/en). Vacio = seguir al del sistema.</summary>
    public const string KeyLanguage = "user.language";
    public const string KeyLlmEndpoint = "ai.endpoint";
    public const string KeyLlmModel = "ai.model";
    public const string KeySound = "celebration.sound";
    public const string KeyHaptics = "celebration.haptics";
    public const string KeyNotifyEnabled = "notify.enabled";
    public const string KeyNotifyHour = "notify.hour";
    public const string KeyHotkey = "desktop.hotkey";
    public const string KeyStartWithWindows = "desktop.autostart";

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
