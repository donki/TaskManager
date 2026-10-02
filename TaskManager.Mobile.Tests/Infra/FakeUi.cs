using TaskManager.Mobile.Helpers;
using TaskManager.Mobile.Services;

namespace TaskManager.Mobile.Tests.Infra;

/// <summary>Un dialogo que la pagina ha enseñado, con lo que se le ha contestado.</summary>
public sealed record Dialog(string Kind, string? Title, string? Message, string[] Options, object? Answer);

/// <summary>
/// El sistema de mentira: apunta cada navegacion, dialogo, copia o apertura, y contesta a los
/// dialogos con lo que la prueba haya preparado (<see cref="Answer"/>). Un dialogo sin respuesta
/// preparada se descarta, como tocar fuera de la tarjeta.
/// </summary>
public sealed class FakeUi : IUiPlatform
{
    private readonly Queue<object?> _answers = new();

    public List<string> Routes { get; } = [];

    public List<Page> Pushed { get; } = [];

    public int Pops { get; private set; }

    public List<Dialog> Dialogs { get; } = [];

    public List<string> Clipboard { get; } = [];

    public List<string> Opened { get; } = [];

    public List<(string Text, string Subject, string Title)> Shared { get; } = [];

    public List<bool> Haptics { get; } = [];

    public int Hidden { get; private set; }

    public string VersionString { get; set; } = "2026.10.02.00";

    public string CacheDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "tm-mobile-cache");

    /// <summary>La imagen que «hay» en el portapapeles.</summary>
    public (byte[] Bytes, string Extension)? ClipboardImage { get; set; }

    /// <summary>El fichero que «elige» el usuario. Null: cancela.</summary>
    public PickedFile? FileToPick { get; set; }

    public bool CameraAllowed { get; set; } = true;

    /// <summary>Si no es null, se lanza al abrir algo (navegador, fichero, correo...).</summary>
    public Exception? OpenFails { get; set; }

    /// <summary>Si no es null, se lanza al navegar.</summary>
    public Exception? NavigationFails { get; set; }

    /// <summary>Lo que se hara al apilar una pagina (p. ej. leer un QR en la de la camara).</summary>
    public Func<Page, Task>? OnPush { get; set; }

    /// <summary>Respuestas para los proximos dialogos, en orden: bool, texto u opcion, o null.</summary>
    public FakeUi Answer(params object?[] answers)
    {
        foreach (var answer in answers)
        {
            _answers.Enqueue(answer);
        }

        return this;
    }

    public int UnusedAnswers => _answers.Count;

    private object? Next() => _answers.Count > 0 ? _answers.Dequeue() : null;

    public Task GoToAsync(string route)
    {
        if (NavigationFails is { } ex)
        {
            return Task.FromException(ex);
        }

        Routes.Add(route);
        return Task.CompletedTask;
    }

    public async Task PushAsync(Page from, Page page)
    {
        Pushed.Add(page);
        if (OnPush is { } action)
        {
            await action(page);
        }
    }

    public Task PopAsync(Page from)
    {
        Pops++;
        return Task.CompletedTask;
    }

    public async Task<bool> AlertAsync(Page page, string title, string message, string accept, string? cancel = null)
    {
        await Task.Yield();
        var answer = Next() as bool? ?? false;
        Dialogs.Add(new Dialog("alert", title, message, cancel is null ? [accept] : [accept, cancel], answer));
        return answer;
    }

    public async Task<string?> ActionSheetAsync(Page page, string? title, string cancel, params string[] options)
    {
        await Task.Yield();
        var answer = Next() as string;
        Dialogs.Add(new Dialog("sheet", title, null, options, answer));
        return answer;
    }

    public async Task<string?> PromptAsync(Page page, string title, string? message, string accept = "OK",
        string cancel = "Cancel", string? initialValue = null, string? placeholder = null)
    {
        await Task.Yield();
        var answer = Next() as string;
        Dialogs.Add(new Dialog("prompt", title, message, [initialValue ?? string.Empty, placeholder ?? string.Empty], answer));
        return answer;
    }

    public void BeginInvokeOnMainThread(Action action)
    {
        if (SynchronizationContext.Current is { } context)
        {
            context.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }

    public Task SetClipboardTextAsync(string text)
    {
        Clipboard.Add(text);
        return Task.CompletedTask;
    }

    /// <summary>Si no es null, se lanza al leer la imagen del portapapeles.</summary>
    public Exception? ClipboardFails { get; set; }

    public Task<(byte[] Bytes, string Extension)?> ReadClipboardImageAsync() =>
        ClipboardFails is { } ex
            ? Task.FromException<(byte[] Bytes, string Extension)?>(ex)
            : Task.FromResult(ClipboardImage);

    private Task Open(string what)
    {
        if (OpenFails is { } ex)
        {
            return Task.FromException(ex);
        }

        Opened.Add(what);
        return Task.CompletedTask;
    }

    public Task OpenBrowserAsync(string url) => Open("browser:" + url);

    public Task OpenUriAsync(Uri uri) => Open("uri:" + uri);

    public Task OpenFileAsync(string title, string path) => Open("file:" + path);

    public Task ComposeEmailAsync(string subject, string to) => Open($"mail:{to}:{subject}");

    public Task<PickedFile?> PickFileAsync() => Task.FromResult(FileToPick);

    public Task<bool> CameraAllowedAsync() => Task.FromResult(CameraAllowed);

    public void Haptic(bool strong) => Haptics.Add(strong);

    public Task ShareTextAsync(string text, string subject, string title)
    {
        Shared.Add((text, subject, title));
        return Task.CompletedTask;
    }

    public void HideApp() => Hidden++;
}

/// <summary>Los avisos de mentira: apunta lo que se programa, se cancela y se enseña.</summary>
public sealed class FakeReminders : IReminderPlatform
{
    public bool Allowed { get; set; }

    public bool GrantOnRequest { get; set; } = true;

    public int Requests { get; private set; }

    public Dictionary<int, (ReminderAlarm Alarm, DateTime Moment)> Scheduled { get; } = [];

    public List<int> Cancelled { get; } = [];

    public List<(int Id, string Title, string Text)> Shown { get; } = [];

    /// <summary>Si no es null, preguntar por el permiso lanza esto.</summary>
    public Exception? Fails { get; set; }

    public Task<bool> IsAllowedAsync() => Fails is { } ex ? Task.FromException<bool>(ex) : Task.FromResult(Allowed);

    public Task<bool> RequestPermissionAsync()
    {
        Requests++;
        Allowed = GrantOnRequest;
        return Task.FromResult(Allowed);
    }

    public void Schedule(int requestCode, ReminderAlarm alarm, DateTime moment) => Scheduled[requestCode] = (alarm, moment);

    public void Cancel(int requestCode)
    {
        Scheduled.Remove(requestCode);
        Cancelled.Add(requestCode);
    }

    public void Show(int id, string title, string text) => Shown.Add((id, title, text));
}
