using System.ComponentModel;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;

namespace TaskManager.Mobile.Localization;

/// <summary>
/// Puente entre el servicio de idiomas del nucleo y el XAML.
/// </summary>
/// <remarks>
/// Se expone como indexador y como origen de enlace: el XAML pide <c>{loc:T Clave}</c>, que por
/// dentro es un Binding a <c>[Clave]</c> sobre esta instancia. Al cambiar de idioma basta con
/// avisar de que "el indexador entero" ha cambiado y **todas** las pantallas se repintan solas, sin
/// reiniciar la aplicacion ni tener que recordar llamar a un ApplyTexts en cada una.
/// </remarks>
public sealed class Loc : INotifyPropertyChanged
{
    private static Loc? _instance;

    private LocalizationService? _service;

    private Loc()
    {
    }

    public static Loc Instance => _instance ??= new Loc();

    public event PropertyChangedEventHandler? PropertyChanged;

    private LocalizationService Service =>
        _service ??= ServiceHelper.GetRequiredService<LocalizationService>();

    public string this[string key] => Service[key];

    public string Language => Service.Language;

    public string Format(string key, params object[] args) => Service.Format(key, args);

    public async Task SetLanguageAsync(string language)
    {
        await Service.SetLanguageAsync(language);

        // El aviso TIENE que salir del hilo de interfaz: el servicio usa ConfigureAwait(false), asi
        // que aqui ya se esta en un hilo del pool y MAUI ignora los cambios de enlace que llegan
        // desde fuera del hilo principal (se veia como que el idioma se guardaba pero la pantalla
        // seguia igual).
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // "Item[]" es la forma de decirle a los enlaces que TODO el indexador cambio.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        });
    }
}
