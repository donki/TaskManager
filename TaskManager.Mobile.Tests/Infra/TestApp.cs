using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Dispatching;
using TaskManager.Core.Data;
using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;
using TaskManager.Mobile.Services;
using TaskManager.Tests;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TaskManager.Mobile.Tests.Infra;

/// <summary>
/// La aplicacion montada para una prueba: el mismo contenedor que <c>MauiProgram</c>
/// (<see cref="MauiProgram.AddServices"/>) sobre una carpeta temporal, con dobles solo en lo que
/// toca el sistema: red (<see cref="FakeHttp"/>), navegador, avisos e interfaz del sistema.
/// </summary>
/// <remarks>
/// Las pruebas van en serie: la aplicacion tiene estado global (<c>Application.Current</c>,
/// <c>ServiceHelper</c>, <c>Ui.Platform</c>) igual que en el movil, donde solo hay una.
/// </remarks>
internal sealed class TestApp : IAsyncDisposable
{
    private static bool _dispatcherReady;

    private TestApp(string folder, ServiceProvider services, FakeUi ui, FakeReminders reminders, FakeHttp http, FakeMail mail, ScriptedBrowser browser)
    {
        Browser = browser;
        Folder = folder;
        Services = services;
        Ui = ui;
        Reminders = reminders;
        Http = http;
        Mail = mail;
    }

    public string Folder { get; }

    public ServiceProvider Services { get; }

    public FakeUi Ui { get; }

    public FakeReminders Reminders { get; }

    public FakeHttp Http { get; }

    public FakeMail Mail { get; }

    /// <summary>El navegador de las entradas (cuenta y correo).</summary>
    public ScriptedBrowser Browser { get; }

    public SupabaseAuthService Auth => Services.GetRequiredService<SupabaseAuthService>();

    /// <summary>
    /// Deja a alguien dentro con una cuenta «de verdad» (no local) sin red: la sesion guardada se
    /// recupera y la renovacion falla por falta de red, que es justo el caso en que se sigue con lo
    /// guardado.
    /// </summary>
    public async Task SignInStoredAsync(string provider = "Microsoft", string email = "ana@example.com",
        string name = "Ana", string avatar = "https://example.com/ana.png")
    {
        await Settings.SetAsync(SettingsService.KeyAuthProvider, provider);
        await Settings.SetAsync(SettingsService.KeyAccountEmail, email);
        await Settings.SetAsync(SettingsService.KeyDisplayName, name);
        await Settings.SetAsync(SettingsService.KeyAvatarUrl, avatar);
        await Services.GetRequiredService<ITokenStore>().SetAsync("auth.google_refresh", "refresco");
        Http.Throw(HttpMethod.Post, "/");
        Assert.NotNull(await Auth.RestoreSessionAsync());
    }

    public App? Application { get; private set; }

    public string DatabasePath => Path.Combine(Folder, MauiProgram.DatabaseName);

    public TaskService Tasks => Services.GetRequiredService<TaskService>();

    public TaskRepository Repository => Services.GetRequiredService<TaskRepository>();

    public SettingsService Settings => Services.GetRequiredService<SettingsService>();

    public LocalizationService Texts => Services.GetRequiredService<LocalizationService>();

    /// <param name="account">La cuenta que esta dentro. Vacia = nadie ha entrado todavia.</param>
    public static async Task<TestApp> StartAsync(string account = "cuenta-a", string language = "es", bool withApp = true)
    {
        if (!_dispatcherReady)
        {
            DispatcherProvider.SetCurrent(new TestDispatcherProvider());
            _dispatcherReady = true;
        }

        TestDispatcher.Instance.Timers.Clear();
        Controls.CelebrationView.BadgeHold = TimeSpan.FromMilliseconds(5);

        var folder = Path.Combine(Path.GetTempPath(), $"tm-mobile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        var ui = new FakeUi { CacheDirectory = Path.Combine(folder, "cache") };
        var reminders = new FakeReminders();
        var http = new FakeHttp();
        var mail = new FakeMail();
        var browser = new ScriptedBrowser();

        var collection = new ServiceCollection();
        MauiProgram.AddServices(collection, folder);

        // Solo se cambia lo que sale del proceso: ni red, ni navegador, ni avisos de verdad.
        collection.AddSingleton(http.Client());
        collection.AddSingleton<IReminderPlatform>(reminders);
        collection.AddSingleton<IOAuthBrowser>(browser);
        collection.AddSingleton<IMailReader>(mail);
        collection.AddSingleton(sp => new MailOAuthService(
            sp.GetRequiredService<HttpClient>(), browser, sp.GetRequiredService<ITokenStore>()));

        var services = collection.BuildServiceProvider();
        ServiceHelper.Initialize(services);
        Helpers.Ui.Platform = ui;

        var app = new TestApp(folder, services, ui, reminders, http, mail, browser);

        var settings = app.Settings;
        await settings.LoadAsync();
        if (account.Length > 0)
        {
            await settings.SetAsync(SettingsService.KeyGoogleSub, account);
            await settings.SetAsync(SettingsService.KeyUserId, account);
        }

        await settings.SetAsync(SettingsService.KeyLanguage, language);
        await app.Tasks.InitializeAsync();

        if (withApp)
        {
            app.Application = new App();
        }

        return app;
    }

    /// <summary>Una lista con tareas, para no repetir lo mismo en cada prueba.</summary>
    public async Task<(TaskList List, List<TaskItem> Tasks)> SeedAsync(string listName, params string[] titles)
    {
        var list = await Repository.CreateListAsync(listName);
        var tasks = new List<TaskItem>();
        foreach (var title in titles)
        {
            tasks.Add(await Repository.AddTaskAsync(list.Id, title));
        }

        return (list, tasks);
    }

    public async ValueTask DisposeAsync()
    {
        Services.GetService<SyncCoordinator>()?.Stop();
        await Services.GetRequiredService<LocalDatabase>().Connection.CloseAsync();
        await Services.DisposeAsync();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // Windows puede tardar en soltar el fichero; es temporal y lo limpia el sistema.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Un navegador que contesta lo que diga la prueba (por defecto, la vuelta con un codigo).</summary>
public sealed class ScriptedBrowser : IOAuthBrowser
{
    public string RedirectUri => "com.socratic.taskmanager://auth";

    public List<Uri> Opened { get; } = [];

    public Func<Uri, Task<Uri>> Respond { get; set; } =
        authorize => Task.FromResult(new Uri("com.socratic.taskmanager://auth?code=codigo-1&state=x"));

    public Task<Uri> AuthenticateAsync(Uri authorizeUrl, CancellationToken cancellationToken = default)
    {
        Opened.Add(authorizeUrl);
        return Respond(authorizeUrl);
    }
}

/// <summary>El buzon de mentira: devuelve lo preparado, o lanza lo preparado.</summary>
public sealed class FakeMail : IMailReader
{
    public List<MailMessage> Messages { get; } = [];

    public Exception? Fails { get; set; }

    public List<(MailAccount Account, string Secret, bool UseOAuth)> Calls { get; } = [];

    public Task<IReadOnlyList<MailMessage>> FetchAsync(MailAccount account, string secret, int take = 25,
        bool onlyUnread = false, bool useOAuth = false, CancellationToken cancellationToken = default)
    {
        Calls.Add((account, secret, useOAuth));
        return Fails is { } ex
            ? Task.FromException<IReadOnlyList<MailMessage>>(ex)
            : Task.FromResult<IReadOnlyList<MailMessage>>(Messages.Take(take).ToList());
    }
}

/// <summary>Atajos para mover las paginas como lo haria el usuario.</summary>
public static class PageDriver
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>El control con ese x:Name.</summary>
    public static T Named<T>(this Element root, string name) where T : class =>
        root.FindByName<T>(name) ?? throw new InvalidOperationException($"No hay ningun {typeof(T).Name} llamado {name}.");

    /// <summary>
    /// Llama a un metodo (normalmente un manejador privado) y espera a que acabe todo lo que
    /// ponga en marcha, tambien si es <c>async void</c>.
    /// </summary>
    public static async Task<object?> Call(this object target, string method, params object?[] args)
    {
        var result = await target.Invoke(method, args);
        await UiThread.IdleAsync();
        return result;
    }

    /// <summary>
    /// Como <see cref="Call"/> pero sin esperar a los <c>async void</c>: para usarlo desde dentro de
    /// otro manejador que sigue en marcha (esperar ahi seria esperarse a si mismo).
    /// </summary>
    public static async Task<object?> Invoke(this object target, string method, params object?[] args)
    {
        var type = target as Type ?? target.GetType();
        var instance = target is Type ? null : target;
        MethodInfo? info = null;
        for (var t = type; t is not null && info is null; t = t.BaseType)
        {
            info = t.GetMethods(Any | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == args.Length);
        }

        if (info is null)
        {
            throw new MissingMethodException(type.Name, method);
        }

        object? result;
        try
        {
            result = info.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        if (result is Task task)
        {
            await task;
            result = task.GetType().IsGenericType ? task.GetType().GetProperty("Result")!.GetValue(task) : null;
        }

        return result;
    }

    /// <summary>Espera (bombeando la cola) a que se cumpla una condicion.</summary>
    public static async Task Until(Func<bool> condition, int timeoutSeconds = 10)
    {
        var limit = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition())
        {
            if (DateTime.UtcNow > limit)
            {
                throw new TimeoutException("La condicion no se cumplio a tiempo.");
            }

            await Task.Delay(2);
        }
    }

    /// <summary>Un manejador de evento con el emisor y los argumentos de siempre.</summary>
    public static Task<object?> Handler(this object target, string method, object? sender = null, EventArgs? e = null) =>
        target.Call(method, sender, e ?? EventArgs.Empty);

    /// <summary>La pagina aparece en pantalla.</summary>
    public static Task Appear(this Page page)
    {
        TestHandler.Attach(page);
        return page.Call("OnAppearing");
    }

    /// <summary>Pulsa un boton (con su evento Clicked) y espera a lo que haga.</summary>
    public static async Task Click(this Button button)
    {
        button.SendClicked();
        await UiThread.IdleAsync();
    }

    public static async Task Click(this ImageButton button)
    {
        button.SendClicked();
        await UiThread.IdleAsync();
    }

    /// <summary>Toca una vista con un TapGestureRecognizer (el primero que tenga).</summary>
    public static async Task Tap(this View view)
    {
        var tap = view.GestureRecognizers.OfType<TapGestureRecognizer>().First();
        var send = typeof(TapGestureRecognizer).GetMethod("SendTapped", Any)!;
        send.Invoke(tap, [view, null]);
        await UiThread.IdleAsync();
    }

    /// <summary>Cambia un valor y espera a los manejadores que dispare.</summary>
    public static async Task Set(Action change)
    {
        change();
        await UiThread.IdleAsync();
    }

    public static T Field<T>(this object target, string name)
    {
        for (var t = target.GetType(); t is not null; t = t.BaseType)
        {
            if (t.GetField(name, Any | BindingFlags.DeclaredOnly) is { } field)
            {
                return (T)field.GetValue(target)!;
            }
        }

        throw new MissingFieldException(target.GetType().Name, name);
    }

    public static void SetField(this object target, string name, object? value)
    {
        for (var t = target.GetType(); t is not null; t = t.BaseType)
        {
            if (t.GetField(name, Any | BindingFlags.DeclaredOnly) is { } field)
            {
                field.SetValue(target, value);
                return;
            }
        }

        throw new MissingFieldException(target.GetType().Name, name);
    }

    /// <summary>Lanza un evento (sin handler de plataforma, MAUI no lanza algunos solo).</summary>
    public static async Task Raise(this object target, string eventName, object? e = null)
    {
        for (var t = target.GetType(); t is not null; t = t.BaseType)
        {
            if (t.GetField(eventName, Any | BindingFlags.DeclaredOnly) is { } field)
            {
                (field.GetValue(target) as Delegate)?.DynamicInvoke(target, e ?? EventArgs.Empty);
                await UiThread.IdleAsync();
                return;
            }
        }

        throw new MissingFieldException(target.GetType().Name, eventName);
    }

    /// <summary>Boton con un parametro, como los de las filas de una lista.</summary>
    public static ImageButton RowButton(object parameter) => new() { CommandParameter = parameter };
}
