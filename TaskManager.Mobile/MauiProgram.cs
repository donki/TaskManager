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
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // El lector de QR de las invitaciones a un grupo. Los lectores de codigos del movil
        // abren direcciones web y una invitacion no lo es, asi que el lector va aqui dentro.
        builder.UseBarcodeReader();

        // Servicios (constitucion 5 y 7: la logica vive aqui, las paginas solo la orquestan).
#if DEMO
        // La demostracion no comparte base con la de verdad: se instala encima para hacer las
        // capturas y las tareas de quien la usa tienen que seguir donde estaban.
        builder.Services.AddSingleton(_ => new LocalDatabase(
            Path.Combine(FileSystem.AppDataDirectory, "taskmanager-demo.db3")));
#else
        builder.Services.AddSingleton(_ => new LocalDatabase(
            Path.Combine(FileSystem.AppDataDirectory, "taskmanager.db3")));
#endif
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
        // El correo (oculto) vuelve por el esquema propio de la aplicacion.
        builder.Services.AddSingleton(services => new MailOAuthService(
            services.GetRequiredService<HttpClient>(),
            services.GetRequiredService<Services.MauiOAuthBrowser>(),
            services.GetRequiredService<ITokenStore>()));
        builder.Services.AddSingleton<TaskService>();

        // Entrada con Google o con Microsoft: pestaña del navegador del sistema y vuelta por el
        // esquema propio (Microsoft) o por el identificador invertido del cliente de escritorio de
        // Google, que no valida paquete ni huella. El mismo navegador sirve para el correo.
        //
        // Hasta el 2026-09-12 la entrada volvia a un servidor local en 127.0.0.1, como en Windows.
        // En la tablet Samsung (Android 16) no llegaba nunca: con la pestaña delante la aplicacion
        // cuenta como «en segundo plano» y el cortafuegos del sistema —ahorro de bateria y
        // restriccion de red de fondo— le tira hasta los paquetes de loopback. Por intent no hay red
        // que cortar.
        builder.Services.AddSingleton<Services.MauiOAuthBrowser>();
        builder.Services.AddSingleton<IOAuthBrowser>(services => services.GetRequiredService<Services.MauiOAuthBrowser>());
        builder.Services.AddSingleton<ITokenStore>(services =>
            new Services.SecureTokenStore(new SettingsTokenStore(services.GetRequiredService<SettingsService>())));
        builder.Services.AddSingleton<SupabaseAuthService>();

        // Quien decide cuando se sincroniza. Antes en Android no se sincronizaba nunca: por eso el
        // mismo usuario veia listas distintas en el movil y en Windows.
        builder.Services.AddSingleton<SyncCoordinator>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<MyTasksPage>();
        builder.Services.AddTransient<ListsPage>();
        builder.Services.AddTransient<KanbanPage>();
        builder.Services.AddTransient<ListDetailPage>();
        builder.Services.AddTransient<TaskDetailPage>();
        builder.Services.AddTransient<MailPage>();
        builder.Services.AddTransient<GroupsPage>();
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
