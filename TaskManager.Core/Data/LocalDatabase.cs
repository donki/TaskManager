using SQLite;
using TaskManager.Core.Models;

namespace TaskManager.Core.Data;

/// <summary>
/// Almacen local. Es la fuente de verdad en el dispositivo: la interfaz lee y escribe siempre aqui
/// y la sincronizacion va por detras, de modo que el funcionamiento sin red no es un modo aparte.
/// </summary>
public sealed class LocalDatabase
{
    private readonly SQLiteAsyncConnection _connection;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _ready;

    public LocalDatabase(string databasePath)
    {
        DatabasePath = databasePath;
        _connection = new SQLiteAsyncConnection(
            databasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
    }

    public string DatabasePath { get; }

    public SQLiteAsyncConnection Connection => _connection;

    public async Task InitializeAsync()
    {
        if (_ready)
        {
            return;
        }

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_ready)
            {
                return;
            }

            await _connection.CreateTablesAsync(
                CreateFlags.None,
                typeof(TaskGroup),
                typeof(GroupMember),
                typeof(TaskList),
                typeof(TaskItem),
                typeof(TaskStep),
                typeof(XpEvent),
                typeof(SettingEntry),
                typeof(SyncOp)).ConfigureAwait(false);

            _ready = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
