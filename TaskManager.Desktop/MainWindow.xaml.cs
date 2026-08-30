using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskManager.Core.Models;
using TaskManager.Core.Services;

namespace TaskManager.Desktop;

/// <summary>
/// Ventana principal de Windows: listas, correo, Azure DevOps y gremio.
/// </summary>
/// <remarks>
/// <para>El panel de la bandeja sirve para capturar y completar en dos segundos, y para eso esta
/// bien. Todo lo demas —revisar listas, convertir correos en tareas, traer elementos de DevOps—
/// necesita sitio y no cabia en el, asi que Windows se habia quedado corto frente al movil.</para>
///
/// <para>Va en pestañas y no en cinco ventanas sueltas: son cosas que se miran seguidas y abrir y
/// cerrar ventanas para pasar de una a otra molesta mas de lo que ordena.</para>
/// </remarks>
public partial class MainWindow : Window
{
    private readonly TaskService _tasks;
    private readonly SettingsService _settings;
    private readonly IMailReader _mail;
    private readonly MailOAuthService _mailOAuth;
    private readonly AzureDevOpsService _devops;

    private readonly ObservableCollection<ListRow> _lists = [];
    private readonly ObservableCollection<TaskRow> _listTasks = [];
    private readonly ObservableCollection<MailRow> _mails = [];
    private readonly ObservableCollection<DevOpsRow> _devopsItems = [];

    private Guid _selectedList;
    private string? _mailOAuthToken;
    private string? _devopsToken;

    public MainWindow(TaskService tasks, SettingsService settings, IMailReader mail,
        MailOAuthService mailOAuth, AzureDevOpsService devops)
    {
        InitializeComponent();

        _tasks = tasks;
        _settings = settings;
        _mail = mail;
        _mailOAuth = mailOAuth;
        _devops = devops;

        ListsBox.ItemsSource = _lists;
        ListTasksBox.ItemsSource = _listTasks;
        MailBox.ItemsSource = _mails;
        DevOpsBox.ItemsSource = _devopsItems;
    }

    private static string T(string key) => Localization.Loc.Get(key);

    private static string F(string key, params object[] args) => Localization.Loc.Format(key, args);

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        MailAddressBox.Text = _settings.Get("mail.address", string.Empty);
        MailPortBox.Text = "993";

        await ReloadListsAsync();
        await ReloadBoardAsync();
        await RestoreDevOpsAsync();
    }

    // =======================================================================
    // Listas
    // =======================================================================

    private async Task ReloadListsAsync()
    {
        var selected = _selectedList;

        _lists.Clear();

        foreach (var list in await _tasks.Repository.GetPrivateListsAsync())
        {
            var tasks = await _tasks.Repository.GetTasksAsync(list.Id);
            var pending = tasks.Count(t => !t.IsDone);

            _lists.Add(new ListRow(list.Id, list.Name, tasks.Count == 0
                ? T("ListEmpty")
                : pending == 0
                    ? F("ListAllDone", tasks.Count)
                    : F("ListPending", pending, tasks.Count)));
        }

        // Se conserva la lista que estuviera abierta: recargar no deberia devolverte al principio
        // cada vez que marcas una tarea.
        var row = _lists.FirstOrDefault(l => l.Id == selected) ?? _lists.FirstOrDefault();
        ListsBox.SelectedItem = row;
    }

    private async void OnListSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ListsBox.SelectedItem is ListRow row)
        {
            _selectedList = row.Id;
            await ReloadListTasksAsync();
        }
    }

    private async Task ReloadListTasksAsync()
    {
        _listTasks.Clear();

        if (_selectedList == Guid.Empty)
        {
            return;
        }

        foreach (var task in await _tasks.Repository.GetTasksAsync(_selectedList))
        {
            _listTasks.Add(new TaskRow(task.Id, task.Title, task.IsDone));
        }
    }

    private async void OnNewListClick(object sender, RoutedEventArgs e)
    {
        var name = Prompt.Ask(this, T("NewListTitle"), T("ListNamePlaceholder"));
        if (!string.IsNullOrWhiteSpace(name))
        {
            await _tasks.Repository.CreateListAsync(name);
            await ReloadListsAsync();
        }
    }

    private async void OnAddTaskClick(object sender, RoutedEventArgs e) => await AddTaskAsync();

    private async void OnNewTaskKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await AddTaskAsync();
        }
    }

    private async Task AddTaskAsync()
    {
        var title = NewTaskBox.Text.Trim();
        if (title.Length == 0 || _selectedList == Guid.Empty)
        {
            return;
        }

        await _tasks.Repository.AddTaskAsync(_selectedList, title);
        NewTaskBox.Text = string.Empty;

        await ReloadListTasksAsync();
        await ReloadListsAsync();
    }

    private async void OnTaskToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: Guid id })
        {
            return;
        }

        var task = await _tasks.Repository.GetTaskAsync(id);
        if (task is null)
        {
            return;
        }

        // Completar celebra y suma XP; deshacer lo devuelve sin castigar.
        if (task.IsDone)
        {
            await _tasks.UncompleteTaskAsync(task);
        }
        else
        {
            await _tasks.CompleteTaskAsync(task);
        }

        await ReloadListTasksAsync();
        await ReloadListsAsync();
        await ReloadBoardAsync();
    }

    // =======================================================================
    // Correo
    // =======================================================================

    private async void OnMailGoogleClick(object sender, RoutedEventArgs e) =>
        await MailSignInAsync(MailOAuthProvider.Google);

    private async void OnMailMicrosoftClick(object sender, RoutedEventArgs e) =>
        await MailSignInAsync(MailOAuthProvider.Microsoft);

    private async Task MailSignInAsync(MailOAuthProvider provider)
    {
        MailStatus.Text = T("MailSigningIn");

        try
        {
            var session = await _mailOAuth.SignInAsync(provider);
            _mailOAuthToken = session.AccessToken;
            MailStatus.Text = T("MailSignedIn");

            await ReadMailAsync(provider);
        }
        catch (TaskCanceledException)
        {
            MailStatus.Text = T("SignInCancelled");
        }
        catch (Exception ex)
        {
            MailStatus.Text = ex.Message;
        }
    }

    private async void OnMailReadClick(object sender, RoutedEventArgs e) => await ReadMailAsync(null);

    private async Task ReadMailAsync(MailOAuthProvider? provider)
    {
        var address = MailAddressBox.Text.Trim();
        if (address.Length == 0)
        {
            MailStatus.Text = T("MailMissingFields");
            return;
        }

        // El preajuste sale del dominio; lo que se escriba a mano manda sobre el.
        var account = MailProviders.ForAddress(address);

        if (MailHostBox.Text.Trim() is { Length: > 0 } host)
        {
            account = account with { ImapHost = host };
        }

        if (int.TryParse(MailPortBox.Text.Trim(), out var port) && port > 0)
        {
            account = account with { ImapPort = port };
        }

        var useOAuth = _mailOAuthToken is not null && provider is not null;
        var secret = useOAuth ? _mailOAuthToken! : MailPasswordBox.Password;

        if (secret.Length == 0)
        {
            MailStatus.Text = T("MailMissingFields");
            return;
        }

        MailReadButton.IsEnabled = false;
        MailStatus.Text = T("MailReading");

        try
        {
            var messages = await _mail.FetchAsync(account, secret, useOAuth: useOAuth);

            _mails.Clear();
            foreach (var message in messages)
            {
                _mails.Add(new MailRow(
                    message.Uid,
                    message.Subject.Length > 0 ? message.Subject : T("MailNoSubject"),
                    $"{message.From} · {message.Date.LocalDateTime:g}",
                    message));
            }

            MailStatus.Text = _mails.Count == 0 ? T("MailNone") : F("MailCount", _mails.Count);

            await _settings.SetAsync("mail.address", address);
        }
        catch (Exception ex)
        {
            MailStatus.Text = ex.Message;
        }
        finally
        {
            MailReadButton.IsEnabled = true;
        }
    }

    private async void OnMailToTaskClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: uint uid })
        {
            return;
        }

        var row = _mails.FirstOrDefault(m => m.Uid == uid);
        if (row is null)
        {
            return;
        }

        var task = await CreateTaskAsync(row.Message.ToTaskTitle(), "correo");
        task.Context = row.Message.ToTaskContext();
        await _tasks.Repository.UpdateTaskAsync(task);

        MailStatus.Text = F("TaskCreated", task.Title);
        await ReloadListsAsync();
    }

    // =======================================================================
    // Azure DevOps
    // =======================================================================

    private async Task RestoreDevOpsAsync()
    {
        if (!AzureDevOpsService.IsConfigured)
        {
            DevOpsSignInButton.IsEnabled = false;
            DevOpsRefreshButton.IsEnabled = false;
            DevOpsStatus.Text = T("DevOpsNotConfigured");
            return;
        }

        var session = await _devops.RestoreAsync();
        if (session is not null)
        {
            _devopsToken = session.AccessToken;
            await LoadDevOpsAsync();
        }
    }

    private async void OnDevOpsSignInClick(object sender, RoutedEventArgs e)
    {
        DevOpsStatus.Text = T("MailSigningIn");

        try
        {
            var session = await _devops.SignInAsync();
            _devopsToken = session.AccessToken;
            await LoadDevOpsAsync();
        }
        catch (TaskCanceledException)
        {
            DevOpsStatus.Text = T("SignInCancelled");
        }
        catch (Exception ex)
        {
            DevOpsStatus.Text = ex.Message;
        }
    }

    private async void OnDevOpsRefreshClick(object sender, RoutedEventArgs e) => await LoadDevOpsAsync();

    private async Task LoadDevOpsAsync()
    {
        if (_devopsToken is null)
        {
            return;
        }

        DevOpsRefreshButton.IsEnabled = false;
        DevOpsStatus.Text = T("DevOpsLoading");

        try
        {
            var items = await _devops.GetAssignedAsync(_devopsToken);

            _devopsItems.Clear();
            foreach (var item in items)
            {
                _devopsItems.Add(new DevOpsRow(
                    item.Id,
                    item.TaskTitle,
                    string.Join(" · ", new[] { item.Project, item.Type, item.State }.Where(s => s.Length > 0)),
                    item));
            }

            DevOpsStatus.Text = _devopsItems.Count == 0
                ? T("DevOpsNone")
                : F("DevOpsCount", _devopsItems.Count);
        }
        catch (Exception ex)
        {
            DevOpsStatus.Text = ex.Message;
        }
        finally
        {
            DevOpsRefreshButton.IsEnabled = true;
        }
    }

    private async void OnDevOpsImportClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int id })
        {
            return;
        }

        var row = _devopsItems.FirstOrDefault(d => d.Id == id);
        if (row is null)
        {
            return;
        }

        var task = await CreateTaskAsync(row.Title, "devops");
        task.Context = F("DevOpsContext", row.Item.Organization, row.Item.Project, row.Item.Url);
        task.DueAt = row.Item.DueDate;
        await _tasks.Repository.UpdateTaskAsync(task);

        DevOpsStatus.Text = F("TaskCreated", task.Title);
        await ReloadListsAsync();
    }

    // =======================================================================
    // Gremio
    // =======================================================================

    private async Task ReloadBoardAsync()
    {
        var board = await _tasks.GetBoardAsync();

        BoardLevel.Text = F("Level", board.Level);
        BoardProgress.Value = board.ProgressInLevel;
        BoardToNext.Text = F("ToNextLevel", board.XpToNextLevel, board.Level + 1);
        BoardXp.Text = F("XpTotal", board.TotalXp);

        BoardStreak.Text = board.CurrentStreak switch
        {
            0 => T("NoStreak"),
            1 => T("StreakOne"),
            _ => F("StreakMany", board.CurrentStreak),
        };

        BoardNextUnlock.Text = board.NextUnlock is { } next
            ? F("NextUnlock", next.Name, next.Level)
            : T("AllUnlocked");
    }

    // =======================================================================

    /// <summary>
    /// Crea la tarea en la primera lista privada, en Mi Dia y con su etiqueta de origen.
    /// </summary>
    /// <remarks>
    /// Si no hay ninguna lista se crea una: importar un correo no deberia fallar por no haber
    /// pasado antes por la pantalla de listas.
    /// </remarks>
    private async Task<TaskItem> CreateTaskAsync(string title, string tag)
    {
        var lists = await _tasks.Repository.GetPrivateListsAsync();
        var listId = lists.FirstOrDefault()?.Id
            ?? (await _tasks.Repository.CreateListAsync(T("DefaultListName"))).Id;

        var task = await _tasks.Repository.AddTaskAsync(listId, title, inMyDay: true);
        task.Tags = TaskTags.FromInput(tag);
        return task;
    }

    // Filas de cada lista de la ventana.
    private sealed record ListRow(Guid Id, string Name, string Caption);

    private sealed record TaskRow(Guid Id, string Title, bool IsDone);

    private sealed record MailRow(uint Uid, string Subject, string Caption, MailMessage Message);

    private sealed record DevOpsRow(int Id, string Title, string Caption, WorkItem Item);
}

/// <summary>
/// Cuadro para pedir un texto. WPF no trae ninguno, y abrir un formulario entero para escribir el
/// nombre de una lista es desproporcionado.
/// </summary>
internal static class Prompt
{
    public static string? Ask(Window owner, string title, string hint)
    {
        var box = new TextBox { Padding = new Thickness(6, 4, 6, 4), FontSize = 13 };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 12, 0, 0) };

        var window = new Window
        {
            Title = title,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            Background = owner.Background,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock
                    {
                        Text = hint,
                        Margin = new Thickness(0, 0, 0, 6),
                        Foreground = System.Windows.Media.Brushes.Gray,
                        FontSize = 11,
                    },
                    box,
                    ok,
                },
            },
        };

        ok.Click += (_, _) => window.DialogResult = true;

        box.Focus();
        return window.ShowDialog() == true && box.Text.Trim().Length > 0 ? box.Text.Trim() : null;
    }
}
