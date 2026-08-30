using System;
using Microsoft.Maui.Controls.Xaml;

namespace TaskManager.Mobile.Localization;

/// <summary>
/// Texto traducido en XAML: <c>Text="{loc:T MenuMyDay}"</c>.
/// </summary>
/// <remarks>
/// Devuelve un Binding en vez de una cadena suelta. Esa es la diferencia entre traducir al cargar
/// la pantalla y traducir de verdad: como el enlace queda vivo sobre <see cref="Loc.Instance"/>,
/// cambiar de idioma repinta lo que ya esta en pantalla, sin volver a entrar en la pagina.
/// </remarks>
[ContentProperty(nameof(Key))]
public sealed class TExtension : IMarkupExtension<BindingBase>
{
    public string Key { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider) => new Binding
    {
        Path = $"[{Key}]",
        Source = Loc.Instance,
        Mode = BindingMode.OneWay,
    };

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) =>
        ProvideValue(serviceProvider);
}
