using TaskManager.Core.Data;
using TaskManager.Core.Services;

namespace TaskManager.Tests;

/// <summary>
/// Una base SQLite de usar y tirar, en un fichero temporal propio de cada prueba: nada toca la base
/// real de la aplicacion y las pruebas pueden correr en paralelo sin pisarse.
/// </summary>
internal sealed class TestStore : IAsyncDisposable
{
    private TestStore(string path)
    {
        Path = path;
        Db = new LocalDatabase(path);
        Settings = new SettingsService(Db);
        Repository = new TaskRepository(Db, Settings);
        Texts = new LocalizationService(Settings);
    }

    public string Path { get; }
    public LocalDatabase Db { get; }
    public SettingsService Settings { get; }
    public TaskRepository Repository { get; }
    public LocalizationService Texts { get; }

    /// <param name="account">La cuenta que esta dentro. Vacia = nadie ha entrado todavia.</param>
    public static async Task<TestStore> CreateAsync(string account = "cuenta-a", string language = "es")
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tm-test-{Guid.NewGuid():N}.db");
        var store = new TestStore(path);
        await store.Settings.LoadAsync();
        if (account.Length > 0)
        {
            await store.Settings.SetAsync(SettingsService.KeyGoogleSub, account);
            await store.Settings.SetAsync(SettingsService.KeyUserId, account);
        }

        await store.Settings.SetAsync(SettingsService.KeyLanguage, language);
        return store;
    }

    public TaskService NewService(INotificationService? notifications = null) =>
        new(Repository, Settings, notifications);

    public async ValueTask DisposeAsync()
    {
        await Db.Connection.CloseAsync();
        foreach (var file in new[] { Path, Path + "-wal", Path + "-shm", Path + "-journal" })
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Windows puede tardar en soltar el fichero; es temporal y lo limpia el sistema.
            }
        }
    }
}
