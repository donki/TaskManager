using System.Windows.Markup;
using TaskManager.Core.Services;

namespace TaskManager.Desktop.Localization;

/// <summary>
/// Acceso a los textos desde la aplicacion de escritorio.
/// </summary>
/// <remarks>
/// El servicio de idiomas vive en el nucleo y lo comparten movil y Windows, de modo que una cadena
/// traducida una vez vale para las dos y no pueden acabar diciendo cosas distintas. Aqui solo hay
/// un punto de entrada estatico, porque el XAML de WPF no tiene inyeccion de dependencias.
/// </remarks>
public static class Loc
{
    private static LocalizationService? _service;

    /// <summary>Lo llama el arranque en cuanto existe el servicio.</summary>
    public static void Use(LocalizationService service) => _service = service;

    public static string Language => _service?.Language ?? "es";

    public static string Get(string key) => _service is null ? key : _service[key];

    public static string Format(string key, params object[] args) =>
        _service is null ? key : _service.Format(key, args);
}

/// <summary>
/// <c>{loc:T Clave}</c> en el XAML.
/// </summary>
/// <remarks>
/// Devuelve la cadena ya resuelta, no un enlace. En Windows el cambio de idioma vuelve a crear las
/// ventanas —igual que en el movil se reconstruye el Shell—, asi que no hace falta que cada texto
/// se quede escuchando: seria mas maquinaria para el mismo resultado.
/// </remarks>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
