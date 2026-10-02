namespace TaskManager.Mobile.Helpers;

/// <summary>
/// Lo que las pantallas le piden al sistema: navegar, preguntar, abrir, copiar, compartir...
/// </summary>
/// <remarks>
/// <para><b>Por que una interfaz.</b> Todo esto solo funciona con la aplicacion corriendo en un
/// movil: fuera de el, MAUI no tiene Shell al que navegar, ni portapapeles, ni camara. Con las
/// paginas llamando a esto en vez de a MAUI directamente, las pruebas construyen las paginas de
/// verdad, pulsan sus botones y comprueban lo que hacen, con un doble en lugar del sistema
/// (<c>TaskManager.Mobile.Tests</c>).</para>
///
/// <para>En la aplicacion siempre es <see cref="MauiUiPlatform"/>, que hace exactamente lo que
/// hacian antes las paginas.</para>
/// </remarks>
public interface IUiPlatform
{
    /// <summary>Navega por el Shell (<c>Shell.Current.GoToAsync</c>).</summary>
    Task GoToAsync(string route);

    /// <summary>Apila una pagina encima de <paramref name="from"/>.</summary>
    Task PushAsync(Page from, Page page);

    /// <summary>Quita la pagina de arriba de la pila de <paramref name="from"/>.</summary>
    Task PopAsync(Page from);

    /// <summary>Aviso o confirmacion (<c>SocShared.ModernDialog.AlertAsync</c>).</summary>
    Task<bool> AlertAsync(Page page, string title, string message, string accept, string? cancel = null);

    /// <summary>Lista de opciones (<c>SocShared.ModernDialog.ActionSheetAsync</c>).</summary>
    Task<string?> ActionSheetAsync(Page page, string? title, string cancel, params string[] options);

    /// <summary>Pide un texto (<c>SocShared.ModernDialog.PromptAsync</c>).</summary>
    Task<string?> PromptAsync(Page page, string title, string? message, string accept = "OK",
        string cancel = "Cancel", string? initialValue = null, string? placeholder = null);

    /// <summary>Ejecuta en el hilo de la interfaz.</summary>
    void BeginInvokeOnMainThread(Action action);

    /// <summary>Version de la aplicacion instalada.</summary>
    string VersionString { get; }

    /// <summary>Carpeta de cache de la aplicacion.</summary>
    string CacheDirectory { get; }

    Task SetClipboardTextAsync(string text);

    /// <summary>La imagen del portapapeles (bytes y extension), o null si no hay.</summary>
    Task<(byte[] Bytes, string Extension)?> ReadClipboardImageAsync();

    /// <summary>Abre una direccion en el navegador del sistema.</summary>
    Task OpenBrowserAsync(string url);

    /// <summary>Abre una direccion con la aplicacion que le toque.</summary>
    Task OpenUriAsync(Uri uri);

    /// <summary>Abre un fichero del disco con la aplicacion que le toque.</summary>
    Task OpenFileAsync(string title, string path);

    /// <summary>Abre el correo para escribir a <paramref name="to"/>.</summary>
    Task ComposeEmailAsync(string subject, string to);

    /// <summary>Elige un fichero. Null si se cancela.</summary>
    Task<PickedFile?> PickFileAsync();

    /// <summary>Si hay permiso de camara; si no lo hay, lo pide.</summary>
    Task<bool> CameraAllowedAsync();

    /// <summary>Vibracion corta (o larga, al subir de nivel). Sin vibrador no hace nada.</summary>
    void Haptic(bool strong);

    /// <summary>Comparte un texto por donde el usuario elija.</summary>
    Task ShareTextAsync(string text, string subject, string title);

    /// <summary>Oculta la aplicacion sin cerrarla (atras en la pantalla de inicio).</summary>
    void HideApp();
}

/// <summary>Un fichero elegido por el usuario.</summary>
public sealed record PickedFile(string FileName, Func<Task<Stream>> OpenReadAsync);

/// <summary>
/// El sistema en uso. La aplicacion no lo cambia nunca; las pruebas ponen su doble.
/// </summary>
public static class Ui
{
    public static IUiPlatform Platform { get; set; } = new MauiUiPlatform();
}

/// <summary>El de verdad: MAUI y los dialogos de SocShared.</summary>
public sealed class MauiUiPlatform : IUiPlatform
{
    public Task GoToAsync(string route) => Shell.Current.GoToAsync(route);

    public Task PushAsync(Page from, Page page) => from.Navigation.PushAsync(page);

    public Task PopAsync(Page from) => from.Navigation.PopAsync();

    public Task<bool> AlertAsync(Page page, string title, string message, string accept, string? cancel = null) =>
        SocShared.ModernDialog.AlertAsync(page, title, message, accept, cancel);

    public Task<string?> ActionSheetAsync(Page page, string? title, string cancel, params string[] options) =>
        SocShared.ModernDialog.ActionSheetAsync(page, title, cancel, options);

    public Task<string?> PromptAsync(Page page, string title, string? message, string accept = "OK",
        string cancel = "Cancel", string? initialValue = null, string? placeholder = null) =>
        SocShared.ModernDialog.PromptAsync(page, title, message, accept, cancel, initialValue, placeholder);

    public void BeginInvokeOnMainThread(Action action) => MainThread.BeginInvokeOnMainThread(action);

    public string VersionString => AppInfo.Current.VersionString;

    public string CacheDirectory => FileSystem.CacheDirectory;

    public Task SetClipboardTextAsync(string text) => Clipboard.Default.SetTextAsync(text);

    public Task<(byte[] Bytes, string Extension)?> ReadClipboardImageAsync() => Services.ClipboardImage.ReadAsync();

    public Task OpenBrowserAsync(string url) => Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);

    public Task OpenUriAsync(Uri uri) => Launcher.Default.OpenAsync(uri);

    public Task OpenFileAsync(string title, string path) =>
        Launcher.Default.OpenAsync(new OpenFileRequest(title, new ReadOnlyFile(path)));

    public Task ComposeEmailAsync(string subject, string to) =>
        Email.Default.ComposeAsync(new EmailMessage { Subject = subject, To = [to] });

    public async Task<PickedFile?> PickFileAsync() =>
        await FilePicker.Default.PickAsync() is { } picked ? new PickedFile(picked.FileName, picked.OpenReadAsync) : null;

    public async Task<bool> CameraAllowedAsync()
    {
        var permiso = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (permiso != PermissionStatus.Granted)
        {
            permiso = await Permissions.RequestAsync<Permissions.Camera>();
        }

        return permiso == PermissionStatus.Granted;
    }

    public void Haptic(bool strong)
    {
        try
        {
            HapticFeedback.Default.Perform(strong ? HapticFeedbackType.LongPress : HapticFeedbackType.Click);
        }
        catch (FeatureNotSupportedException)
        {
            // Dispositivo sin vibrador: la celebracion visual se basta.
        }
    }

    public Task ShareTextAsync(string text, string subject, string title) =>
        Share.Default.RequestAsync(new ShareTextRequest { Text = text, Subject = subject, Title = title });

    public void HideApp()
    {
#if ANDROID
        Platform.CurrentActivity?.MoveTaskToBack(true);
#endif
    }
}
