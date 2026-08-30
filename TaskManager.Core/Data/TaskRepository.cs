using TaskManager.Core.Models;

namespace TaskManager.Core.Data;

/// <summary>
/// Acceso a listas, tareas y pasos. Todo cambio deja ademas una fila en <c>sync_queue</c> para que
/// la sincronizacion pueda subirlo cuando haya red.
/// </summary>
public sealed class TaskRepository
{
    private readonly LocalDatabase _db;

    public TaskRepository(LocalDatabase db) => _db = db;

    private SQLite.SQLiteAsyncConnection Db => _db.Connection;

    public Task InitializeAsync() => _db.InitializeAsync();

    // -----------------------------------------------------------------------
    // Listas
    // -----------------------------------------------------------------------

    public Task<List<TaskList>> GetPrivateListsAsync() =>
        Db.Table<TaskList>()
          .Where(l => l.GroupId == null && !l.Deleted)
          .OrderBy(l => l.SortOrder)
          .ToListAsync();

    public Task<List<TaskList>> GetGroupListsAsync(Guid groupId) =>
        Db.Table<TaskList>()
          .Where(l => l.GroupId == groupId && !l.Deleted)
          .OrderBy(l => l.SortOrder)
          .ToListAsync();

    public async Task<TaskList?> GetListAsync(Guid id) =>
        await Db.Table<TaskList>().Where(l => l.Id == id).FirstOrDefaultAsync().ConfigureAwait(false);

    public async Task<TaskList> CreateListAsync(string name, Guid? groupId = null, string icon = "ic_list")
    {
        var order = await Db.Table<TaskList>().CountAsync().ConfigureAwait(false);
        var list = new TaskList
        {
            Name = name.Trim(),
            GroupId = groupId,
            Icon = icon,
            SortOrder = order,
        };

        await Db.InsertAsync(list).ConfigureAwait(false);
        await QueueAsync("task_lists", list.Id, "upsert").ConfigureAwait(false);
        return list;
    }

    public async Task UpdateListAsync(TaskList list)
    {
        list.UpdatedAt = DateTime.UtcNow;
        await Db.UpdateAsync(list).ConfigureAwait(false);
        await QueueAsync("task_lists", list.Id, "upsert").ConfigureAwait(false);
    }

    /// <summary>Borrado logico: hay que poder propagar la baja a los demas dispositivos.</summary>
    public async Task DeleteListAsync(TaskList list)
    {
        list.Deleted = true;
        list.UpdatedAt = DateTime.UtcNow;
        await Db.UpdateAsync(list).ConfigureAwait(false);
        await Db.ExecuteAsync("UPDATE tasks SET Deleted = 1 WHERE ListId = ?", list.Id).ConfigureAwait(false);
        await QueueAsync("task_lists", list.Id, "delete").ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Tareas
    // -----------------------------------------------------------------------

    public async Task<List<TaskItem>> GetTasksAsync(Guid listId, bool includeDone = true, string? tag = null)
    {
        var query = Db.Table<TaskItem>().Where(t => t.ListId == listId && !t.Deleted);
        if (!includeDone)
        {
            query = query.Where(t => !t.IsDone);
        }

        var tasks = await query.OrderBy(t => t.IsDone)
                               .ThenBy(t => t.SortOrder)
                               .ThenByDescending(t => t.CreatedAt)
                               .ToListAsync().ConfigureAwait(false);

        tasks = FilterByTag(tasks, tag);
        await FillStepCountsAsync(tasks).ConfigureAwait(false);
        return tasks;
    }

    /// <summary>
    /// El filtro por etiqueta se aplica en memoria: una lista de tareas cabe de sobra y asi la
    /// comparacion respeta mayusculas y tildes igual que en el resto de la aplicacion.
    /// </summary>
    private static List<TaskItem> FilterByTag(List<TaskItem> tasks, string? tag) =>
        string.IsNullOrWhiteSpace(tag)
            ? tasks
            : tasks.Where(t => TaskTags.Has(t.Tags, tag)).ToList();

    /// <summary>Etiquetas en uso, ordenadas, para ofrecerlas como filtro.</summary>
    public async Task<List<string>> GetTagsAsync()
    {
        var stored = await Db.QueryScalarsAsync<string>(
            "SELECT Tags FROM tasks WHERE Deleted = 0 AND Tags <> ''").ConfigureAwait(false);

        return stored
            .SelectMany(TaskTags.Split)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// "Mi Dia" del dia indicado (por defecto hoy). No hay lista fisica: es la fecha del campo.
    /// </summary>
    public async Task<List<TaskItem>> GetMyDayAsync(DateTime? day = null, string? tag = null)
    {
        var date = (day ?? DateTime.Now).Date;
        var tasks = await Db.Table<TaskItem>()
                            .Where(t => !t.Deleted && t.MyDayOn == date)
                            .OrderBy(t => t.IsDone)
                            .ThenBy(t => t.SortOrder)
                            .ThenByDescending(t => t.CreatedAt)
                            .ToListAsync().ConfigureAwait(false);

        tasks = FilterByTag(tasks, tag);
        await FillStepCountsAsync(tasks).ConfigureAwait(false);
        return tasks;
    }

    public Task<int> CountMyDayPendingAsync()
    {
        var date = DateTime.Now.Date;
        return Db.Table<TaskItem>().Where(t => !t.Deleted && !t.IsDone && t.MyDayOn == date).CountAsync();
    }

    public async Task<TaskItem?> GetTaskAsync(Guid id) =>
        await Db.Table<TaskItem>().Where(t => t.Id == id).FirstOrDefaultAsync().ConfigureAwait(false);

    public async Task<TaskItem> AddTaskAsync(Guid listId, string title, bool inMyDay = false)
    {
        var task = new TaskItem
        {
            ListId = listId,
            Title = title.Trim(),
            MyDayOn = inMyDay ? DateTime.Now.Date : null,
            // Lo nuevo entra arriba, que es donde estaba antes por fecha de creacion.
            SortOrder = await NextTopOrderAsync(listId).ConfigureAwait(false),
        };

        await Db.InsertAsync(task).ConfigureAwait(false);
        await QueueAsync("tasks", task.Id, "upsert").ConfigureAwait(false);
        return task;
    }

    /// <summary>Hueco libre por encima de todo lo que hay en la lista.</summary>
    private async Task<int> NextTopOrderAsync(Guid listId)
    {
        var lowest = await Db.ExecuteScalarAsync<int?>(
            "SELECT MIN(SortOrder) FROM tasks WHERE ListId = ? AND Deleted = 0", listId)
            .ConfigureAwait(false);

        return (lowest ?? 0) - 1;
    }

    /// <summary>
    /// Fija el orden manual a partir de como han quedado las tareas tras arrastrar.
    /// </summary>
    /// <remarks>
    /// Se renumera la lista entera de 0 en adelante en vez de tocar solo la que se ha movido: con
    /// numeros consecutivos no hay empates ni huecos que se agoten, y una lista de tareas es
    /// pequeña de sobra para que renumerarla salga gratis. Se escribe en una transaccion para que
    /// no pueda quedarse a medias y dejar el orden inconsistente.
    /// </remarks>
    public async Task ReorderTasksAsync(IReadOnlyList<Guid> orderedIds)
    {
        if (orderedIds.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;

        await Db.RunInTransactionAsync(db =>
        {
            for (var i = 0; i < orderedIds.Count; i++)
            {
                db.Execute("UPDATE tasks SET SortOrder = ?, UpdatedAt = ? WHERE Id = ?",
                    i, now, orderedIds[i]);
            }
        }).ConfigureAwait(false);

        foreach (var id in orderedIds)
        {
            await QueueAsync("tasks", id, "upsert").ConfigureAwait(false);
        }
    }

    /// <summary>Da de alta una tarea ya construida (la siguiente vuelta de una repetitiva).</summary>
    public async Task<TaskItem> AddTaskCopyAsync(TaskItem task)
    {
        await Db.InsertAsync(task).ConfigureAwait(false);
        await QueueAsync("tasks", task.Id, "upsert").ConfigureAwait(false);
        return task;
    }

    public async Task UpdateTaskAsync(TaskItem task)
    {
        task.UpdatedAt = DateTime.UtcNow;
        await Db.UpdateAsync(task).ConfigureAwait(false);
        await QueueAsync("tasks", task.Id, "upsert").ConfigureAwait(false);
    }

    public async Task DeleteTaskAsync(TaskItem task)
    {
        task.Deleted = true;
        task.UpdatedAt = DateTime.UtcNow;
        await Db.UpdateAsync(task).ConfigureAwait(false);
        await QueueAsync("tasks", task.Id, "delete").ConfigureAwait(false);
    }

    public Task ToggleMyDayAsync(TaskItem task)
    {
        task.MyDayOn = task.MyDayOn?.Date == DateTime.Now.Date ? null : DateTime.Now.Date;
        return UpdateTaskAsync(task);
    }

    // -----------------------------------------------------------------------
    // Pasos
    // -----------------------------------------------------------------------

    public Task<List<TaskStep>> GetStepsAsync(Guid taskId) =>
        Db.Table<TaskStep>()
          .Where(s => s.TaskId == taskId && !s.Deleted)
          .OrderBy(s => s.SortOrder)
          .ToListAsync();

    public async Task<TaskStep?> GetStepAsync(Guid id) =>
        await Db.Table<TaskStep>().Where(s => s.Id == id).FirstOrDefaultAsync().ConfigureAwait(false);

    public async Task<List<TaskStep>> AddStepsAsync(Guid taskId, IEnumerable<string> titles, string source = "manual")
    {
        var existing = await Db.Table<TaskStep>().Where(s => s.TaskId == taskId).CountAsync().ConfigureAwait(false);
        var steps = titles
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Select((t, i) => new TaskStep
            {
                TaskId = taskId,
                Title = t,
                SortOrder = existing + i,
                Source = source,
            })
            .ToList();

        if (steps.Count == 0)
        {
            return steps;
        }

        await Db.InsertAllAsync(steps).ConfigureAwait(false);
        foreach (var step in steps)
        {
            await QueueAsync("task_steps", step.Id, "upsert").ConfigureAwait(false);
        }

        return steps;
    }

    public async Task UpdateStepAsync(TaskStep step)
    {
        step.UpdatedAt = DateTime.UtcNow;
        await Db.UpdateAsync(step).ConfigureAwait(false);
        await QueueAsync("task_steps", step.Id, "upsert").ConfigureAwait(false);
    }

    public async Task DeleteStepAsync(TaskStep step)
    {
        step.Deleted = true;
        step.UpdatedAt = DateTime.UtcNow;
        await Db.UpdateAsync(step).ConfigureAwait(false);
        await QueueAsync("task_steps", step.Id, "delete").ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Grupos (en local; el alta real contra Supabase la hace ISyncService)
    // -----------------------------------------------------------------------

    public Task<List<TaskGroup>> GetGroupsAsync() =>
        Db.Table<TaskGroup>().Where(g => !g.Deleted).OrderBy(g => g.Name).ToListAsync();

    public async Task<TaskGroup?> GetGroupAsync(Guid id) =>
        await Db.Table<TaskGroup>().Where(g => g.Id == id).FirstOrDefaultAsync().ConfigureAwait(false);

    public async Task<TaskGroup> SaveGroupAsync(TaskGroup group)
    {
        group.UpdatedAt = DateTime.UtcNow;
        await Db.InsertOrReplaceAsync(group).ConfigureAwait(false);
        return group;
    }

    public async Task DeleteGroupAsync(TaskGroup group)
    {
        group.Deleted = true;
        group.UpdatedAt = DateTime.UtcNow;
        await Db.UpdateAsync(group).ConfigureAwait(false);
        await Db.ExecuteAsync("UPDATE task_lists SET Deleted = 1 WHERE GroupId = ?", group.Id).ConfigureAwait(false);
    }

    public Task<List<GroupMember>> GetMembersAsync(Guid groupId) =>
        Db.Table<GroupMember>().Where(m => m.GroupId == groupId).OrderBy(m => m.DisplayName).ToListAsync();

    public Task SaveMemberAsync(GroupMember member) => Db.InsertOrReplaceAsync(member);

    // -----------------------------------------------------------------------
    // XP
    // -----------------------------------------------------------------------

    public async Task AddXpAsync(XpEvent xp)
    {
        await Db.InsertAsync(xp).ConfigureAwait(false);
        await QueueAsync("xp_events", xp.Id, "upsert").ConfigureAwait(false);
    }

    public async Task<int> GetTotalXpAsync(Guid? groupId = null)
    {
        var sql = groupId is null
            ? "SELECT COALESCE(SUM(Amount), 0) FROM xp_events"
            : "SELECT COALESCE(SUM(Amount), 0) FROM xp_events WHERE GroupId = ?";

        return groupId is null
            ? await Db.ExecuteScalarAsync<int>(sql).ConfigureAwait(false)
            : await Db.ExecuteScalarAsync<int>(sql, groupId.Value).ConfigureAwait(false);
    }

    public Task<List<XpEvent>> GetRecentXpAsync(int take = 20) =>
        Db.Table<XpEvent>().OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync();

    /// <summary>Dias distintos (hora local) con al menos una tarea completada, mas reciente primero.</summary>
    public async Task<List<DateTime>> GetActiveDaysAsync(int take = 90)
    {
        // Las fechas se guardan como ticks, asi que DATE() de SQLite no sirve: hay que traer los
        // DateTime y agrupar en memoria. Con un historial de 90 dias el coste es irrelevante.
        var since = DateTime.UtcNow.AddDays(-take);
        var done = await Db.Table<TaskItem>()
                           .Where(t => t.IsDone && !t.Deleted && t.DoneAt >= since)
                           .ToListAsync().ConfigureAwait(false);

        return done
            .Where(t => t.DoneAt is not null)
            .Select(t => t.DoneAt!.Value.ToLocalTime().Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();
    }

    public async Task<int> CountCompletedAsync(DateTime since)
    {
        return await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM tasks WHERE IsDone = 1 AND Deleted = 0 AND DoneAt >= ?",
            since).ConfigureAwait(false);
    }

    /// <summary>
    /// Traspasa autoria y XP del identificador provisional a la cuenta de Google recien entrada.
    /// Sin esto, entrar por primera vez borraria de un plumazo el nivel y las rachas conseguidas
    /// antes de tener cuenta.
    /// </summary>
    public async Task ReassignUserAsync(string oldUserId, string newUserId)
    {
        if (string.IsNullOrEmpty(oldUserId) || string.IsNullOrEmpty(newUserId) || oldUserId == newUserId)
        {
            return;
        }

        await Db.ExecuteAsync("UPDATE tasks SET CreatedBy = ? WHERE CreatedBy = ?", newUserId, oldUserId)
                .ConfigureAwait(false);
        await Db.ExecuteAsync("UPDATE tasks SET DoneBy = ? WHERE DoneBy = ?", newUserId, oldUserId)
                .ConfigureAwait(false);
        await Db.ExecuteAsync("UPDATE xp_events SET UserId = ? WHERE UserId = ?", newUserId, oldUserId)
                .ConfigureAwait(false);
        await Db.ExecuteAsync("UPDATE task_lists SET OwnerId = ? WHERE OwnerId = ?", newUserId, oldUserId)
                .ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Cola de sincronizacion
    // -----------------------------------------------------------------------

    private Task QueueAsync(string entity, Guid id, string operation) =>
        Db.InsertAsync(new SyncOp { Entity = entity, EntityId = id.ToString(), Operation = operation });

    /// <summary>
    /// Escribe una fila que viene del servidor.
    /// </summary>
    /// <remarks>
    /// No pasa por los metodos normales a proposito: esos encolan la fila para subirla, y aqui
    /// acabaria devolviendole al servidor lo que el servidor acaba de mandar. Ademas se respeta la
    /// marca de tiempo que trae, en vez de ponerle la de ahora, que es lo que permite que la regla
    /// de "gana lo mas reciente" siga teniendo sentido en la siguiente vuelta.
    /// </remarks>
    public Task SaveFromRemoteAsync<T>(T row, bool isNew) where T : notnull =>
        isNew ? Db.InsertAsync(row) : Db.UpdateAsync(row);

    public Task<List<SyncOp>> GetPendingSyncAsync(int take = 200) =>
        Db.Table<SyncOp>().OrderBy(o => o.Id).Take(take).ToListAsync();

    public Task ClearSyncAsync(IEnumerable<SyncOp> ops)
    {
        var ids = ops.Select(o => o.Id).ToList();
        return ids.Count == 0
            ? Task.CompletedTask
            : Db.ExecuteAsync($"DELETE FROM sync_queue WHERE Id IN ({string.Join(",", ids)})");
    }

    // -----------------------------------------------------------------------

    private async Task FillStepCountsAsync(List<TaskItem> tasks)
    {
        foreach (var task in tasks)
        {
            var steps = await GetStepsAsync(task.Id).ConfigureAwait(false);
            task.StepCount = steps.Count;
            task.StepsDone = steps.Count(s => s.IsDone);
        }
    }
}
