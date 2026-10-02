using TaskManager.Core;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using TaskManager.Core.Data;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;
using TaskManager.Mobile.Pages;
using ZXing.Net.Maui.Controls;

namespace TaskManager.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Gestor global de excepciones (General 6.12): lo primero, antes que el builder. Un error que
        // no se esperaba se apunta en crash.log con su traza, se avisa en el idioma de la
        // aplicacion (que no es la cultura del sistema: se elige en Ajustes) y la app sigue.
        SocShared.CrashGuard.Install("Task Manager",
            message: () => Texto("UnexpectedError"),
            language: () => Texto(null));

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // El lector de QR de las invitaciones a un grupo. Los lectores de codigos del movil
        // abren direcciones web y una invitacion no lo es, asi que el lector va aqui dentro.
        builder.UseBarcodeReader();

        // Lo nativo de los avisos; que avisar y cuando lo decide ReminderScheduler.
#if ANDROID
        builder.Services.AddSingleton<Services.IReminderPlatform, Platforms.Android.AndroidReminderPlatform>();
#endif
        AddServices(builder.Services, FileSystem.AppDataDirectory);

#if DEBUG
        builder.Services.AddLogging(logging => logging.AddDebug());
#endif

        var app = builder.Build();
        ServiceHelper.Initialize(app.Services);
        return app;
    }

    /// <summary>
    /// La base de datos. La demostracion no comparte base con la de verdad: se instala encima para
    /// hacer las capturas y las tareas de quien la usa tienen que seguir donde estaban.
    /// </summary>
#if DEMO
    internal const string DatabaseName = "taskmanager-demo.db3";
#else
    internal const string DatabaseName = "taskmanager.db3";
#endif

    /// <summary>
    /// Los servicios de la aplicacion, con la base en <paramref name="dataDirectory"/>. Las pruebas
    /// montan este mismo contenedor sobre una carpeta temporal y cambian solo lo que toca el sistema
    /// (red, navegador, avisos).
    /// </summary>
    public static IServiceCollection AddServices(IServiceCollection services, string dataDirectory)
    {
        // Servicios (constitucion 5 y 7: la logica vive aqui, las paginas solo la orquestan).
        services.AddSingleton(_ => new LocalDatabase(Path.Combine(dataDirectory, DatabaseName)));
        services.AddSingleton<TaskRepository>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<LocalizationService>();

        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(12) });

        // Sincronizacion: la de verdad si hay proyecto de Supabase, y si no la local, que deja la
        // cola esperando sin perder nada. Se decide aqui una vez y ninguna pantalla se entera.
        services.AddSingleton<ISyncService>(sp => SupabaseConfig.IsConfigured
            ? new SupabaseSyncService(
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<TaskRepository>(),
                sp.GetRequiredService<SettingsService>(),
                sp.GetRequiredService<SupabaseAuthService>())
            : new LocalOnlySyncService(sp.GetRequiredService<TaskRepository>()));

        services.AddSingleton<INotificationService>(sp =>
            new Services.ReminderScheduler(sp.GetRequiredService<Services.IReminderPlatform>()));
        services.AddSingleton<IMailReader, MailKitReader>();
        // El correo (oculto) vuelve por el esquema propio de la aplicacion.
        services.AddSingleton(sp => new MailOAuthService(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<Services.MauiOAuthBrowser>(),
            sp.GetRequiredService<ITokenStore>()));
        services.AddSingleton<TaskService>();

        // Entrada con Google o con Microsoft: pestaña del navegador del sistema y vuelta por el
        // esquema propio (Microsoft) o por el identificador invertido del cliente de escritorio de
        // Google, que no valida paquete ni huella. El mismo navegador sirve para el correo.
        //
        // Hasta el 2026-09-12 la entrada volvia a un servidor local en 127.0.0.1, como en Windows.
        // En la tablet Samsung (Android 16) no llegaba nunca: con la pestaña delante la aplicacion
        // cuenta como «en segundo plano» y el cortafuegos del sistema —ahorro de bateria y
        // restriccion de red de fondo— le tira hasta los paquetes de loopback. Por intent no hay red
        // que cortar.
        services.AddSingleton<Services.MauiOAuthBrowser>();
        services.AddSingleton<IOAuthBrowser>(sp => sp.GetRequiredService<Services.MauiOAuthBrowser>());
        services.AddSingleton<ITokenStore>(sp =>
            new Services.SecureTokenStore(new SettingsTokenStore(sp.GetRequiredService<SettingsService>())));
        services.AddSingleton<SupabaseAuthService>();

        // Quien decide cuando se sincroniza. Antes en Android no se sincronizaba nunca: por eso el
        // mismo usuario veia listas distintas en el movil y en Windows.
        services.AddSingleton<SyncCoordinator>();

        services.AddTransient<LoginPage>();
        services.AddTransient<MyTasksPage>();
        services.AddTransient<ListsPage>();
        services.AddTransient<KanbanPage>();
        services.AddTransient<ListDetailPage>();
        services.AddTransient<TaskDetailPage>();
        services.AddTransient<MailPage>();
        services.AddTransient<GroupsPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<AboutPage>();

        return services;
    }

    /// <summary>
    /// Texto para el aviso de error (o, con <c>null</c>, el idioma en uso). Puede llamarse antes de
    /// que el contenedor este listo: entonces se deja el texto por defecto de CrashGuard.
    /// </summary>
    internal static string? Texto(string? key)
    {
        try
        {
            var loc = ServiceHelper.Services?.GetService<LocalizationService>();
            return loc is null ? null : key is null ? loc.Language : loc[key];
        }
        catch
        {
            return null;
        }
    }
}
