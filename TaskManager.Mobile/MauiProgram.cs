using TaskManager.Core;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using TaskManager.Core.Data;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;
using TaskManager.Mobile.Pages;

namespace TaskManager.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // Servicios (constitucion 5 y 7: la logica vive aqui, las paginas solo la orquestan).
        builder.Services.AddSingleton(_ => new LocalDatabase(
            Path.Combine(FileSystem.AppDataDirectory, "taskmanager.db3")));
        builder.Services.AddSingleton<TaskRepository>();
        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<LocalizationService>();

        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(12) });

        // Sincronizacion: la de verdad si hay proyecto de Supabase, y si no la local, que deja la
        // cola esperando sin perder nada. Se decide aqui una vez y ninguna pantalla se entera.
        builder.Services.AddSingleton<ISyncService>(services => SupabaseConfig.IsConfigured
            ? new SupabaseSyncService(
                services.GetRequiredService<HttpClient>(),
                services.GetRequiredService<TaskRepository>(),
                services.GetRequiredService<SettingsService>(),
                services.GetRequiredService<SupabaseAuthService>())
            : new LocalOnlySyncService(services.GetRequiredService<TaskRepository>()));

        // El desglose intenta el modelo local (el PC de la LAN, si esta configurado) y cae a
        // plantillas: en un movil sin conexion el boton de la varita tiene que responder igual.
        builder.Services.AddSingleton<IBreakdownService>(services =>
        {
            var settings = services.GetRequiredService<SettingsService>();
            return new CascadingBreakdownService(
                new LocalLlmBreakdownService(
                    services.GetRequiredService<HttpClient>(),
                    () => settings.LlmEndpoint,
                    () => settings.LlmModel),
                new HeuristicBreakdownService());
        });

        builder.Services.AddSingleton<INotificationService, Platforms.Android.NotificationService>();
        builder.Services.AddSingleton<IMailReader, MailKitReader>();
        // El correo (oculto) sigue con el esquema propio: Microsoft si lo admite y no necesita
        // servidor local.
        builder.Services.AddSingleton(services => new MailOAuthService(
            services.GetRequiredService<HttpClient>(),
            services.GetRequiredService<Services.MauiOAuthBrowser>(),
            services.GetRequiredService<ITokenStore>()));
        builder.Services.AddSingleton<TaskService>();

        // Entrada con Google: navegador del sistema y un servidor local de un solo uso, igual que
        // en Windows. No se usa el esquema de identificador invertido porque eso obliga a un cliente
        // OAuth de tipo Android, que valida paquete y huella SHA-1 y responde
        // «Error 400: invalid_request» en cuanto una de las dos no cuadra (visto el 2026-08-31).
        // Con la loopback vale el mismo cliente de escritorio que ya funciona.
        //
        // MauiOAuthBrowser (esquema propio) se queda para el correo: Microsoft si lo admite.
        builder.Services.AddSingleton<IOAuthBrowser, Services.AndroidLoopbackBrowser>();
        builder.Services.AddSingleton<Services.MauiOAuthBrowser>();
        builder.Services.AddSingleton<ITokenStore>(services =>
            new Services.SecureTokenStore(new SettingsTokenStore(services.GetRequiredService<SettingsService>())));
        builder.Services.AddSingleton<SupabaseAuthService>();

        // Quien decide cuando se sincroniza. Antes en Android no se sincronizaba nunca: por eso el
        // mismo usuario veia listas distintas en el movil y en Windows.
        builder.Services.AddSingleton<SyncCoordinator>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<MyTasksPage>();
        builder.Services.AddTransient<ListsPage>();
        builder.Services.AddTransient<ListDetailPage>();
        builder.Services.AddTransient<TaskDetailPage>();
        builder.Services.AddTransient<MailPage>();
        builder.Services.AddTransient<GroupsPage>();
        builder.Services.AddTransient<BoardPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<AboutPage>();

#if DEBUG
        builder.Services.AddLogging(logging => logging.AddDebug());
#endif

        var app = builder.Build();
        ServiceHelper.Initialize(app.Services);
        return app;
    }
}
