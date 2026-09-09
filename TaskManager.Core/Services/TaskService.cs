using TaskManager.Core.Data;
using TaskManager.Core.Gamification;
using TaskManager.Core.Models;

namespace TaskManager.Core.Services;

/// <summary>
/// Fachada que usan las dos aplicaciones. Aqui vive lo que tiene que pasar igual en Android y en
/// Windows: completar una tarea, encadenar combos, repartir XP y desglosar con IA. Las interfaces
/// solo pintan lo que esta fachada les cuenta.
/// </summary>
public sealed class TaskService
{
    private readonly TaskRepository _repository;
    private readonly SettingsService _settings;
    private readonly IBreakdownService _breakdown;
    private readonly INotificationService? _notifications;

    private DateTime _lastCompletion = DateTime.MinValue;
    private int _chain;

    public TaskService(
        TaskRepository repository,
        SettingsService settings,
        IBreakdownService breakdown,
        INotificationService? notifications = null)
    {
        _repository = repository;
        _settings = settings;
        _breakdown = breakdown;
        _notifications = notifications;
    }

    public TaskRepository Repository => _repository;

    /// <summary>La interfaz se engancha aqui para lanzar confeti, sonido y vibracion.</summary>
    public event EventHandler<Celebration>? Celebrated;

    public async Task InitializeAsync()
    {
        await _repository.InitializeAsync().ConfigureAwait(false);
        await _settings.LoadAsync().ConfigureAwait(false);

        // Al actualizar desde una version sin cuentas separadas, lo que ya habia no es de nadie:
        // se lo queda quien esta dentro. Sin esto, sus listas de siempre no las veria nadie.
        // AdoptAccountAsync ya se encarga tambien de la lista por defecto; si no ha entrado nadie
        // todavia, se crea igual y la adoptara la primera cuenta que entre.
        if (_repository.AccountId.Length > 0)
        {
            await AdoptAccountAsync(_repository.AccountId).ConfigureAwait(false);
            return;
        }

        await EnsureDefaultListAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Deja la cuenta lista para usarse: adopta lo que no es de nadie y se asegura de que tenga
    /// donde escribir.
    /// </summary>
    /// <remarks>
    /// <para>Se llama al entrar y al <b>cambiar de cuenta</b>. Lo hecho antes de tener cuenta
    /// —tareas, autoria y XP— pasa a nombre de la que entra en vez de quedarse huerfano bajo el
    /// identificador provisional del aparato.</para>
    ///
    /// <para><b>No toca lo de la otra cuenta.</b> Cada una tiene sus listas
    /// (<see cref="Data.TaskRepository.ClaimOrphansAsync"/>): cambiar de Google a Microsoft no se
    /// lleva nada de una a la otra, solo cambia lo que se ve.</para>
    /// </remarks>
    public async Task AdoptAccountAsync(string accountUserId)
    {
        if (string.IsNullOrEmpty(accountUserId))
        {
            return;
        }

        var claimed = await _repository
            .ClaimOrphansAsync(accountUserId, _settings.LocalUserId)
            .ConfigureAwait(false);

        // Ya no queda nada provisional que traspasar: dejarlo apuntando a esta cuenta haria que la
        // siguiente entrada se llevara lo suyo, que es justo lo que no puede pasar.
        if (claimed > 0)
        {
            await _settings.SetAsync(SettingsService.KeyLocalUserId, string.Empty).ConfigureAwait(false);
        }

        await EnsureDefaultListAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Sin ninguna lista no hay donde escribir, y una cuenta recien estrenada no puede aparecer
    /// vacia del todo.
    /// </summary>
    private async Task EnsureDefaultListAsync()
    {
        var lists = await _repository.GetPrivateListsAsync().ConfigureAwait(false);
        if (lists.Count == 0)
        {
            await _repository.CreateListAsync("Tareas", null, "ic_list").ConfigureAwait(false);
        }
    }

    // -----------------------------------------------------------------------
    // Completar
    // -----------------------------------------------------------------------

    public Task<Celebration?> CompleteTaskAsync(TaskItem task) => CompleteTaskAsync(task, announce: true);

    /// <param name="announce">
    /// Si se avisa a la pantalla para que celebre. En una seleccion multiple se apaga y se celebra
    /// una sola vez al final: doce confetis seguidos por doce tareas marcadas de golpe no premian,
    /// estorban. El XP se suma igual en las doce.
    /// </param>
    private async Task<Celebration?> CompleteTaskAsync(TaskItem task, bool announce)
    {
        if (task.IsDone)
        {
            return null;
        }

        task.IsDone = true;

        // Terminada deja de estar empezada: el tablero no puede enseñarla en dos columnas.
        task.InProgress = false;
        task.DoneAt = DateTime.UtcNow;
        task.DoneBy = _settings.UserId;
        await _repository.UpdateTaskAsync(task).ConfigureAwait(false);

        // Completada: su aviso ya no tiene sentido.
        _notifications?.CancelTaskReminder(task.Id);

        // Si la tarea se repite, aqui nace la siguiente vuelta. La completada se queda hecha como
        // registro (y contando para rachas y XP) en vez de reabrirse.
        await CreateNextOccurrenceAsync(task).ConfigureAwait(false);

        var celebration = await AwardAsync(XpRules.Task, XpKind.Task, task).ConfigureAwait(false);
        if (announce)
        {
            Celebrated?.Invoke(this, celebration);
        }

        return celebration;
    }

    /// <summary>Marca hechas varias tareas de una vez, con una sola celebracion al final.</summary>
    public async Task<Celebration?> CompleteManyAsync(IEnumerable<Guid> ids)
    {
        Celebration? last = null;

        foreach (var id in ids.Distinct())
        {
            if (await _repository.GetTaskAsync(id).ConfigureAwait(false) is { IsDone: false } task)
            {
                last = await CompleteTaskAsync(task, announce: false).ConfigureAwait(false) ?? last;
            }
        }

        if (last is not null)
        {
            Celebrated?.Invoke(this, last);
        }

        return last;
    }

    /// <summary>Devuelve a pendientes varias tareas. No resta XP, igual que desmarcar una sola.</summary>
    public async Task UncompleteManyAsync(IEnumerable<Guid> ids)
    {
        foreach (var id in ids.Distinct())
        {
            if (await _repository.GetTaskAsync(id).ConfigureAwait(false) is { IsDone: true } task)
            {
                await UncompleteTaskAsync(task).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Crea la siguiente aparicion de una tarea repetitiva. La fecha se cuenta desde el plazo que
    /// tenia (o desde hoy si no tenia), y si esa fecha ya paso se va adelantando hasta la primera
    /// que quede por delante: al completar tarde una tarea semanal olvidada, la siguiente es la que
    /// viene, no cuatro atrasadas de golpe.
    /// </summary>
    private async Task CreateNextOccurrenceAsync(TaskItem task)
    {
        var recurrence = task.Recurrence;
        if (!recurrence.Repeats)
        {
            return;
        }

        // Las de una serie ya tienen todas sus vueltas escritas de antemano
        // (ver <see cref="GenerateSeriesAsync"/>): crear aqui la siguiente seria una de mas.
        if (task.SeriesId is not null)
        {
            return;
        }

        var today = DateTime.Now.Date;
        var next = recurrence.Next(task.DueAt?.Date ?? today);
        while (next.Date < today)
        {
            next = recurrence.Next(next);
        }

        var copy = new TaskItem
        {
            ListId = task.ListId,
            Title = task.Title,
            Notes = task.Notes,
            Tags = task.Tags,
            RecurrenceRule = task.RecurrenceRule,
            DueAt = next,

            // Si estaba planificada, la vuelta siguiente se planifica con el mismo desfase respecto
            // al plazo: quien se organiza dos dias antes lo sigue haciendo dos dias antes.
            PlannedFor = task is { PlannedFor: not null, DueAt: not null }
                ? next - (task.DueAt.Value.Date - task.PlannedFor.Value.Date)
                : null,
            CreatedBy = _settings.UserId,

            // Los micro-pasos NO se copian: son el desglose de aquella vez. La nueva vuelta puede
            // desglosarse otra vez, y con el contexto que tenga entonces.
        };

        await _repository.AddTaskCopyAsync(copy).ConfigureAwait(false);

        // La vuelta nueva ya nace con su plazo, asi que se programa su aviso aqui mismo.
        _notifications?.ScheduleTaskReminder(copy);
    }

    // -----------------------------------------------------------------------
    // Series de repeticion
    // -----------------------------------------------------------------------

    /// <summary>Lo que se hizo al generar una serie, para poder contarlo en la interfaz.</summary>
    /// <param name="Created">Cuantas tareas se han escrito, contando la que ya existia.</param>
    /// <param name="Truncated">
    /// Si el rango daba para mas de <see cref="Recurrence.MaxOccurrences"/> y se ha cortado.
    /// </param>
    public readonly record struct SeriesResult(int Created, bool Truncated)
    {
        public static readonly SeriesResult Nothing = new(0, false);
    }

    /// <summary>
    /// Escribe todas las vueltas de una tarea que se repite, una por cada dia en que toca entre su
    /// fecha de planificacion y su fecha de finalizacion.
    /// </summary>
    /// <remarks>
    /// <para><b>Por que de golpe y no una a una.</b> Una tarea repetitiva era una sola fila que, al
    /// completarla, creaba la siguiente: en la lista solo se veia la vuelta de turno y en el
    /// calendario, un unico dia. Con la serie escrita, «cada martes hasta diciembre» se ve entera
    /// —en la lista, en el tablero y en el mes— y se puede repartir, mover o adelantar una vuelta
    /// suelta sin tocar las demas.</para>
    ///
    /// <para>Por eso las dos fechas son obligatorias: sin la de finalizacion, «todos los dias» no
    /// tiene ultimo dia y no hay serie que escribir, sino una cuenta infinita.</para>
    ///
    /// <para><b>La tarea que se pasa es la primera vuelta</b>: se queda con el primer dia que toca
    /// y las demas son copias suyas. Volver a llamar con la misma tarea rehace lo que queda por
    /// delante y respeta lo ya hecho y lo ya pasado.</para>
    /// </remarks>
    public async Task<SeriesResult> GenerateSeriesAsync(TaskItem task)
    {
        var recurrence = task.Recurrence;

        if (!recurrence.Repeats || task.PlannedFor is not { } from || task.DueAt is not { } to)
        {
            return SeriesResult.Nothing;
        }

        var days = recurrence.Occurrences(from.Date, to.Date).ToList();
        if (days.Count == 0)
        {
            return SeriesResult.Nothing;
        }

        var series = task.SeriesId ?? Guid.NewGuid();

        // Lo que quede en pie de una generacion anterior se quita antes de escribir la nueva, o
        // cambiar la regla sumaria una serie encima de la otra.
        if (task.SeriesId is { } previous)
        {
            await _repository.DeleteFutureSeriesAsync(previous, task.Id).ConfigureAwait(false);
        }

        // La hora se conserva de las fechas que traia: una tarea de «cada martes a las 9» tiene que
        // seguir avisando a las 9 en todas sus vueltas.
        var plannedTime = from.TimeOfDay;
        var dueTime = to.TimeOfDay;

        var already = await _repository.GetSeriesAsync(series).ConfigureAwait(false);
        var taken = already
            .Where(t => t.Id != task.Id)
            .Select(t => (t.PlannedFor ?? t.DueAt)?.Date)
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToHashSet();

        task.SeriesId = series;
        task.PlannedFor = days[0].Add(plannedTime);
        task.DueAt = days[0].Add(dueTime);
        await _repository.UpdateTaskAsync(task).ConfigureAwait(false);

        var created = 1;

        foreach (var day in days.Skip(1))
        {
            // Las vueltas que ya se hicieron —o que ya pasaron— siguen en su sitio y no se repiten.
            if (taken.Contains(day))
            {
                continue;
            }

            var copy = new TaskItem
            {
                ListId = task.ListId,
                Title = task.Title,
                Notes = task.Notes,
                Tags = task.Tags,
                IsPinned = task.IsPinned,
                RecurrenceRule = task.RecurrenceRule,
                SeriesId = series,
                PlannedFor = day.Add(plannedTime),
                DueAt = day.Add(dueTime),
                CreatedBy = _settings.UserId,

                // Los micro-pasos no se copian, igual que en la vuelta suelta: son el desglose de
                // aquel dia, y cada vuelta puede desglosarse con lo que haya entonces.
            };

            await _repository.AddTaskCopyAsync(copy).ConfigureAwait(false);
            _notifications?.ScheduleTaskReminder(copy);
            created++;
        }

        return new SeriesResult(created, days.Count >= Recurrence.MaxOccurrences);
    }

    /// <summary>
    /// Pone o quita el «en curso» del tablero.
    /// </summary>
    /// <remarks>
    /// Empezar una tarea la saca de pendientes sin darla por hecha, asi que ni suma XP ni celebra
    /// nada: lo que se premia es terminar. Y una tarea hecha no puede quedarse en curso, o el
    /// tablero la enseñaria en dos columnas a la vez.
    /// </remarks>
    public async Task SetInProgressAsync(TaskItem task, bool inProgress)
    {
        if (task.InProgress == inProgress && (!inProgress || !task.IsDone))
        {
            return;
        }

        task.InProgress = inProgress;

        if (inProgress && task.IsDone)
        {
            task.IsDone = false;
            task.DoneAt = null;
            task.DoneBy = null;
        }

        await _repository.UpdateTaskAsync(task).ConfigureAwait(false);
    }

    /// <summary>Desmarcar no resta XP: la especificacion pide premiar sin castigar (4.B).</summary>
    public async Task UncompleteTaskAsync(TaskItem task)
    {
        if (!task.IsDone)
        {
            return;
        }

        task.IsDone = false;
        task.DoneAt = null;
        task.DoneBy = null;
        await _repository.UpdateTaskAsync(task).ConfigureAwait(false);
    }

    public async Task<Celebration?> ToggleStepAsync(TaskStep step)
    {
        step.IsDone = !step.IsDone;
        await _repository.UpdateStepAsync(step).ConfigureAwait(false);

        if (!step.IsDone)
        {
            return null;
        }

        var task = await _repository.GetTaskAsync(step.TaskId).ConfigureAwait(false);
        var celebration = await AwardAsync(XpRules.Step, XpKind.Step, task, chain: false).ConfigureAwait(false);
        Celebrated?.Invoke(this, celebration);

        // Cerrar el ultimo micro-paso cierra la tarea: si no, obliga a marcar lo mismo dos veces.
        if (task is { IsDone: false })
        {
            var steps = await _repository.GetStepsAsync(task.Id).ConfigureAwait(false);
            if (steps.Count > 0 && steps.All(s => s.IsDone))
            {
                return await CompleteTaskAsync(task).ConfigureAwait(false) ?? celebration;
            }
        }

        return celebration;
    }

    // -----------------------------------------------------------------------
    // Pasos Magicos
    // -----------------------------------------------------------------------

    /// <summary>
    /// Propone micro-pasos para la tarea **sin guardar nada**. Descarta los que ya estan (comparando
    /// sin tildes ni mayusculas), para no llenar la tarea de duplicados cada vez que se pulsa la
    /// varita. Quien llama ensena la propuesta y decide si incorporarla.
    /// </summary>
    public async Task<BreakdownProposal> ProposeBreakdownAsync(
        TaskItem task,
        CancellationToken cancellationToken = default)
    {
        // El desglose parte de las notas. Antes habia un campo «contexto» aparte solo para esto:
        // eran dos cajas de texto libre en la misma pantalla pidiendo casi lo mismo, y quien
        // escribia en la que no era se quedaba sin pasos utiles.
        var titles = await _breakdown.BreakdownAsync(task.Title, task.Notes, cancellationToken).ConfigureAwait(false);
        if (titles.Count == 0)
        {
            return new BreakdownProposal([], 0, string.Empty);
        }

        var existing = await _repository.GetStepsAsync(task.Id).ConfigureAwait(false);
        var known = existing.Select(s => Normalize(s.Title)).ToHashSet(StringComparer.Ordinal);

        var fresh = new List<string>();
        var repeated = 0;

        foreach (var title in titles)
        {
            if (known.Add(Normalize(title)))
                fresh.Add(title);
            else
                repeated++;
        }

        return new BreakdownProposal(fresh, repeated, _breakdown.Source);
    }

    /// <summary>
    /// Incorpora los pasos aceptados. El XP del desglose se paga una sola vez por tarea; si no,
    /// bastaria repetir el boton para ir sumando.
    /// </summary>
    public async Task<(IReadOnlyList<TaskStep> Steps, Celebration? Celebration)> ApplyBreakdownAsync(
        TaskItem task,
        IReadOnlyList<string> titles)
    {
        if (titles.Count == 0)
        {
            return ([], null);
        }

        var steps = await _repository.AddStepsAsync(task.Id, titles, "ai").ConfigureAwait(false);

        Celebration? celebration = null;
        if (!task.BreakdownRewarded)
        {
            task.BreakdownRewarded = true;
            await _repository.UpdateTaskAsync(task).ConfigureAwait(false);
            celebration = await AwardAsync(XpRules.Breakdown, XpKind.Breakdown, task, chain: false).ConfigureAwait(false);
            Celebrated?.Invoke(this, celebration);
        }

        return (steps, celebration);
    }

    /// <summary>Para comparar pasos: sin tildes, sin mayusculas y sin puntuacion.</summary>
    private static string Normalize(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            var mapped = c switch
            {
                'á' => 'a', 'é' => 'e', 'í' => 'i', 'ó' => 'o', 'ú' => 'u', 'ü' => 'u', 'ñ' => 'n',
                _ => c,
            };

            if (char.IsLetterOrDigit(mapped))
                builder.Append(mapped);
            else if (builder.Length > 0 && builder[^1] != ' ')
                builder.Append(' ');
        }

        return builder.ToString().Trim();
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Reparte XP aplicando el combo. <paramref name="chain"/> a false para lo que no deberia
    /// encadenar racha (pasos y desgloses): si no, marcar cinco micro-pasos dispara un x3 vacio.
    /// </summary>
    private async Task<Celebration> AwardAsync(int baseXp, XpKind kind, TaskItem? task, bool chain = true)
    {
        var now = DateTime.UtcNow;
        if (chain)
        {
            _chain = (now - _lastCompletion).TotalSeconds <= XpRules.ComboWindowSeconds ? _chain + 1 : 0;
            _lastCompletion = now;
        }

        var combo = chain ? XpRules.ComboFor(_chain) : 1.0;
        var amount = (int)Math.Round(baseXp * combo);

        var before = await _repository.GetTotalXpAsync().ConfigureAwait(false);
        var groupId = task is null ? null : await GroupOfAsync(task).ConfigureAwait(false);

        await _repository.AddXpAsync(new XpEvent
        {
            UserId = _settings.UserId,
            GroupId = groupId,
            TaskId = task?.Id,
            Amount = amount,
            Kind = kind,
            Combo = combo,
        }).ConfigureAwait(false);

        var after = before + amount;
        var levelBefore = LevelCurve.LevelFor(before);
        var levelAfter = LevelCurve.LevelFor(after);

        return new Celebration(
            Xp: amount,
            Combo: combo,
            TotalXp: after,
            Level: levelAfter,
            LeveledUp: levelAfter > levelBefore,
            Unlocked: levelAfter > levelBefore
                ? Unlockables.All.FirstOrDefault(u => u.Level == levelAfter)
                : null);
    }

    private async Task<Guid?> GroupOfAsync(TaskItem task)
    {
        var list = await _repository.GetListAsync(task.ListId).ConfigureAwait(false);
        return list?.GroupId;
    }
}

/// <summary>
/// Pasos propuestos por la IA, todavia sin guardar.
/// </summary>
/// <param name="Steps">Los que NO estaban ya en la tarea.</param>
/// <param name="AlreadyPresent">Cuantos se descartaron por estar repetidos.</param>
/// <param name="Source">De donde salieron (modelo local o plantillas), para decirselo al usuario.</param>
public sealed record BreakdownProposal(IReadOnlyList<string> Steps, int AlreadyPresent, string Source)
{
    public bool HasSomethingNew => Steps.Count > 0;
}

