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
        builder.Services.AddSingleton<ISyncService, LocalOnlySyncService>();

        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(12) });

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

        builder.Services.AddSingleton<TaskService>();

        // Entrada con Google a traves de Supabase: navegador del sistema (Custom Tabs) y tokens en
        // el almacen seguro de Android, con la tabla de ajustes como ultimo recurso.
        builder.Services.AddSingleton<IOAuthBrowser, Services.MauiOAuthBrowser>();
        builder.Services.AddSingleton<ITokenStore>(services =>
            new Services.SecureTokenStore(new SettingsTokenStore(services.GetRequiredService<SettingsService>())));
        builder.Services.AddSingleton<SupabaseAuthService>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<MyDayPage>();
        builder.Services.AddTransient<ListsPage>();
        builder.Services.AddTransient<ListDetailPage>();
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
