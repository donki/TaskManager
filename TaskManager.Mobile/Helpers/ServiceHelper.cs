namespace TaskManager.Mobile.Helpers;

/// <summary>
/// Acceso al contenedor de dependencias desde las paginas, que MAUI no inyecta en todos los casos
/// de navegacion (constitucion 5 y 7).
/// </summary>
public static class ServiceHelper
{
    private static IServiceProvider? _services;

    public static void Initialize(IServiceProvider services) => _services = services;

    public static IServiceProvider? Services => _services ??= IPlatformApplication.Current?.Services;

    public static T GetRequiredService<T>() where T : notnull
    {
        var services = _services ?? Services
            ?? throw new InvalidOperationException("ServiceHelper se uso antes de que MauiProgram lo inicializara.");

        return services.GetRequiredService<T>();
    }
}
