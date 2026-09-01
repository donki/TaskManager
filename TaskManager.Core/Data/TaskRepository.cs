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

    /// <summary>
    /// La lista donde caen las tareas cuando nadie ha dicho otra cosa, creandola si hace falta.
    /// </summary>
    /// <remarks>
    /// Existe porque <b>ninguna tarea puede quedarse sin lista</b> y porque escribir la primera
    /// tarea de la vida no puede acabar en un formulario preguntando donde guardarla. El nombre
    /// viene traducido de fuera para que no se cuele una cadena en español en la aplicacion inglesa.
    /// </remarks>
    public async Task<TaskList> GetOrCreateDefaultListAsync(string name)
    {
        var existing = await GetPrivateListsAsync().ConfigureAwait(false);
        return existing.Count > 0 ? existing[0] : await CreateListAsync(name).ConfigureAwait(false);
    }

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

    /// <summary>
    /// Borra una lista y todo lo que cuelga de ella: sus tareas y los pasos de esas tareas.
    /// </summary>
    /// <remarks>
    /// <para><b>Se borra de verdad</b>, aqui y en el servidor. Lo que queda no es la fila sino un
    /// apunte de baja (tabla <c>deletions</c>), que es lo que permite que el otro dispositivo se
    /// entere. Antes era un borrado logico y la fila se quedaba para siempre con
    /// <c>deleted = true</c>: la lista desaparecia de la vista pero seguia ocupando sitio.</para>
    ///
    /// <para><b>Cada fila se encola por separado.</b> Antes las tareas se marcaban de golpe con un
    /// UPDATE y no se encolaba ninguna: en el otro aparato desaparecia la lista pero sus tareas
    /// seguian ahi, sin lista a la que pertenecer, saliendo en «Mis tareas» para siempre.</para>
    /// </remarks>
    /// <param name="moveTasksTo">
    /// A donde van las tareas que quedan dentro. Si es <c>null</c> se borran con la lista.
    /// </param>
    public async Task DeleteListAsync(TaskList list, Guid? moveTasksTo = null)
    {
        var tasks = await Db.Table<TaskItem>().Where(t => t.ListId == list.Id)
                            .ToListAsync().ConfigureAwait(false);

        if (moveTasksTo is { } destination && destination != list.Id)
        {
            foreach (var task in tasks)
            {
                await MoveTaskAsync(task, destination).ConfigureAwait(false);
            }
        }
        else
        {
            foreach (var task in tasks)
            {
                await DeleteTaskAsync(task).ConfigureAwait(false);
            }
        }

        await Db.DeleteAsync(list).ConfigureAwait(false);
        await QueueAsync("task_lists", list.Id, "delete").ConfigureAwait(false);
    }

    /// <summary>
    /// Cambia una tarea de lista.
    /// </summary>
    /// <remarks>
    /// <b>Ninguna tarea puede quedarse sin lista</b>: la lista es de donde cuelga y lo que decide
    /// quien la ve. Por eso este metodo exige un destino, en vez de admitir un hueco.
    /// </remarks>
    public async Task MoveTaskAsync(TaskItem task, Guid listId)
    {
        if (listId == Guid.Empty || listId == task.ListId)
        {
            return;
        }

        task.ListId = listId;
        task.UpdatedAt = DateTime.UtcNow;

        await Db.UpdateAsync(task).ConfigureAwait(false);
        await QueueAsync("tasks", task.Id, "upsert").ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // En bloque (seleccion multiple)
    // -----------------------------------------------------------------------
    //
    // Todo esto se podia hacer ya tarea a tarea, abriendo cada una. La diferencia es el numero de
    // gestos: poner la misma etiqueta a doce tareas eran doce viajes al detalle. Cada metodo recorre
    // las tareas y reusa el guardado de siempre, asi que la marca de tiempo y la cola de
    // sincronizacion salen igual que si se hubieran tocado una por una.

    /// <summary>Añade una etiqueta a varias tareas. Las que ya la tienen se quedan como estan.</summary>
    public async Task<int> AddTagAsync(IEnumerable<Guid> ids, string tag)
    {
        var clean = tag.Trim().Trim(',');
        if (clean.Length == 0)
        {
            return 0;
        }

        var touched = 0;
        foreach (var task in await LoadAsync(ids).ConfigureAwait(false))
        {
            if (TaskTags.Has(task.Tags, clean))
            {
                continue;
            }

            task.Tags = TaskTags.Join([.. task.TagList, clean]);
            await UpdateTaskAsync(task).ConfigureAwait(false);
            touched++;
        }

        return touched;
    }

    /// <summary>Quita una etiqueta de varias tareas.</summary>
    public async Task<int> RemoveTagAsync(IEnumerable<Guid> ids, string tag)
    {
        var touched = 0;
        foreach (var task in await LoadAsync(ids).ConfigureAwait(false))
        {
            if (!TaskTags.Has(task.Tags, tag))
            {
                continue;
            }

            task.Tags = TaskTags.Join(
                task.TagList.Where(t => !string.Equals(t, tag, StringComparison.CurrentCultureIgnoreCase)));
            await UpdateTaskAsync(task).ConfigureAwait(false);
            touched++;
        }

        return touched;
    }

    public async Task<int> SetPinnedAsync(IEnumerable<Guid> ids, bool pinned)
    {
        var touched = 0;
        foreach (var task in await LoadAsync(ids).ConfigureAwait(false))
        {
            if (task.IsPinned == pinned)
            {
                continue;
            }

            task.IsPinned = pinned;
            await UpdateTaskAsync(task).ConfigureAwait(false);
            touched++;
        }

        return touched;
    }

    public async Task<int> MoveTasksAsync(IEnumerable<Guid> ids, Guid listId)
    {
        var touched = 0;
        foreach (var task in await LoadAsync(ids).ConfigureAwait(false))
        {
            if (task.ListId == listId)
            {
                continue;
            }

            await MoveTaskAsync(task, listId).ConfigureAwait(false);
            touched++;
        }

        return touched;
    }

    public async Task<int> DeleteTasksAsync(IEnumerable<Guid> ids)
    {
        var tasks = await LoadAsync(ids).ConfigureAwait(false);
        foreach (var task in tasks)
        {
            await DeleteTaskAsync(task).ConfigureAwait(false);
        }

        return tasks.Count;
    }

    /// <summary>Las tareas de una seleccion, saltandose las que ya no estan.</summary>
    private async Task<List<TaskItem>> LoadAsync(IEnumerable<Guid> ids)
    {
        var tasks = new List<TaskItem>();
        foreach (var id in ids.Distinct())
        {
            if (await GetTaskAsync(id).ConfigureAwait(false) is { } task)
            {
                tasks.Add(task);
            }
        }

        return tasks;
    }

    /// <summary>
    /// Quita una fila que ha llegado borrada de otro dispositivo. No encola nada: la baja ya venia
    /// de fuera, y devolverla al servidor seria darle la vuelta a la misma noticia.
    /// </summary>
    public async Task ApplyRemoteDeleteAsync(string entity, Guid id)
    {
        switch (entity)
        {
            case "tasks":
                await Db.ExecuteAsync("DELETE FROM task_steps WHERE TaskId = ?", id).ConfigureAwait(false);
                await Db.ExecuteAsync("DELETE FROM task_attachments WHERE TaskId = ?", id).ConfigureAwait(false);
                await Db.ExecuteAsync("DELETE FROM tasks WHERE Id = ?", id).ConfigureAwait(false);
                break;

            case "task_attachments":
                await Db.ExecuteAsync("DELETE FROM task_attachments WHERE Id = ?", id).ConfigureAwait(false);
                break;

            case "task_lists":
                await Db.ExecuteAsync(
                    "DELETE FROM task_steps WHERE TaskId IN (SELECT Id FROM tasks WHERE ListId = ?)", id)
                    .ConfigureAwait(false);
                await Db.ExecuteAsync("DELETE FROM tasks WHERE ListId = ?", id).ConfigureAwait(false);
                await Db.ExecuteAsync("DELETE FROM task_lists WHERE Id = ?", id).ConfigureAwait(false);
                break;

            case "task_steps":
                await Db.ExecuteAsync("DELETE FROM task_steps WHERE Id = ?", id).ConfigureAwait(false);
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Tareas
    // -----------------------------------------------------------------------

    /// <param name="search">Texto a buscar en todos los campos, o null para no filtrar.</param>
    public async Task<List<TaskItem>> GetTasksAsync(
        Guid listId, bool includeDone = true, string? tag = null, string? search = null)
    {
        var query = Db.Table<TaskItem>().Where(t => t.ListId == listId && !t.Deleted);
        if (!includeDone)
        {
            query = query.Where(t => !t.IsDone);
        }

        // Mismo criterio que en «Mis tareas»: las ancladas arriba del todo.
        var tasks = await query.OrderBy(t => t.IsDone)
                               .ThenByDescending(t => t.IsPinned)
                               .ThenBy(t => t.SortOrder)
                               .ThenByDescending(t => t.CreatedAt)
                               .ToListAsync().ConfigureAwait(false);

        tasks = FilterByTag(tasks, tag);
        tasks = await FilterBySearchAsync(tasks, search).ConfigureAwait(false);

        await FillStepCountsAsync(tasks).ConfigureAwait(false);
        return tasks;
    }

    /// <summary>
    /// El filtro por etiqueta se aplica en memoria: una lista de tareas cabe de sobra y asi la
    /// comparacion respeta mayusculas y tildes igual que en el resto de la aplicacion.
    /// </summary>
    /// <summary>
    /// Deja solo las tareas donde aparezca <paramref name="search"/>, mirando <b>todo el texto</b>:
    /// el titulo, las notas, las etiquetas, el titulo de sus pasos y el nombre y la direccion de sus
    /// adjuntos.
    /// </summary>
    /// <remarks>
    /// <para>Los pasos y los adjuntos se buscan con una consulta cada uno en vez de recorrerlos
    /// tarea por tarea: recorrerlos serian dos consultas <b>por tarea</b>, y con doscientas tareas
    /// eso se nota al teclear.</para>
    ///
    /// <para>Los campos de la propia tarea se comparan en memoria y no con <c>LIKE</c> porque
    /// <c>LIKE</c> de SQLite solo ignora mayusculas en ASCII: buscar «accion» no encontraria
    /// «Acción». En los pasos y adjuntos se acepta esa limitacion, que es donde menos duele.</para>
    /// </remarks>
    private async Task<List<TaskItem>> FilterBySearchAsync(List<TaskItem> tasks, string? search)
    {
        if (string.IsNullOrWhiteSpace(search) || tasks.Count == 0)
        {
            return tasks;
        }

        var needle = search.Trim();
        var pattern = "%" + needle.Replace("%", "\\%").Replace("_", "\\_") + "%";

        var related = new HashSet<Guid>();

        foreach (var row in await Db.QueryAsync<TaskIdRow>(
            "SELECT DISTINCT TaskId AS Id FROM task_steps WHERE Deleted = 0 AND Title LIKE ? ESCAPE '\\'",
            pattern).ConfigureAwait(false))
        {
            related.Add(row.Id);
        }

        foreach (var row in await Db.QueryAsync<TaskIdRow>(
            "SELECT DISTINCT TaskId AS Id FROM task_attachments " +
            "WHERE Deleted = 0 AND (Name LIKE ? ESCAPE '\\' OR Url LIKE ? ESCAPE '\\')",
            pattern, pattern).ConfigureAwait(false))
        {
            related.Add(row.Id);
        }

        return [.. tasks.Where(t =>
            Has(t.Title, needle) ||
            Has(t.Notes, needle) ||
            Has(t.Tags, needle) ||
            related.Contains(t.Id))];
    }

    private static bool Has(string? text, string needle) =>
        !string.IsNullOrEmpty(text) &&
        text.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Solo para leer identificadores sueltos de una consulta escrita a mano.</summary>
    private sealed class TaskIdRow
    {
        public Guid Id { get; set; }
    }

    /// <summary>
    /// Valor de <c>tag</c> que significa «las que no tienen ninguna etiqueta».
    /// </summary>
    /// <remarks>
    /// Va como una cadena reservada y no como un parametro aparte para que el filtro siga siendo
    /// uno solo: las pantallas guardan «la etiqueta puesta», y esto es una etiqueta mas para ellas.
    /// Empieza por un caracter que no se puede teclear en una etiqueta de verdad, asi que no puede
    /// chocar con ninguna.
    /// </remarks>
    public const string NoTag = "\u0000sin-etiqueta";

    private static List<TaskItem> FilterByTag(List<TaskItem> tasks, string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return tasks;
        }

        // «Sin etiqueta» es lo que hace falta para encontrar lo que se quedo sin clasificar, que es
        // justo lo que se pierde de vista cuando todo lo demas si tiene etiqueta.
        if (tag == NoTag)
        {
            return [.. tasks.Where(t => t.TagList.Count == 0)];
        }

        return [.. tasks.Where(t => t.TagList.Contains(tag, StringComparer.CurrentCultureIgnoreCase))];
    }

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

    /// <summary>
    /// Tareas de un intervalo de dias, agrupadas por dia, para pintar el calendario.
    /// </summary>
    /// <remarks>
    /// <para>Una tarea cae en el dia en el que <b>toca hacerla</b>: la fecha de planificacion si la
    /// tiene y, si no, la de vencimiento. No se pinta en las dos, porque entonces el mes se llena
    /// de duplicados y deja de servir para ver la carga de trabajo, que es justo para lo que se
    /// mira un calendario.</para>
    /// <para>Las repetitivas aparecen solo en su vuelta actual. Proyectar hacia delante todas las
    /// repeticiones daria un calendario lleno de tareas que aun no existen y que, si se cambia la
    /// periodicidad, nunca llegaran a existir.</para>
    /// </remarks>
    public async Task<Dictionary<DateTime, List<TaskItem>>> GetCalendarAsync(
        DateTime from, DateTime to, string? tag = null)
    {
        var start = from.Date;
        var end = to.Date;

        var tasks = await Db.Table<TaskItem>()
                            .Where(t => !t.Deleted)
                            .ToListAsync().ConfigureAwait(false);

        tasks = FilterByTag(tasks, tag);

        var byDay = new Dictionary<DateTime, List<TaskItem>>();

        foreach (var task in tasks)
        {
            var when = (task.PlannedFor ?? task.DueAt)?.Date;
            if (when is null || when < start || when > end)
            {
                continue;
            }

            if (!byDay.TryGetValue(when.Value, out var list))
            {
                byDay[when.Value] = list = [];
            }

            list.Add(task);
        }

        foreach (var list in byDay.Values)
        {
            list.Sort((a, b) => a.IsDone != b.IsDone
                ? a.IsDone.CompareTo(b.IsDone)
                : a.SortOrder.CompareTo(b.SortOrder));
        }

        return byDay;
    }

    /// <summary>
    /// Todas las tareas del usuario, de todas sus listas, pasadas por el filtro elegido.
    /// </summary>
    /// <remarks>
    /// <para>El filtro se aplica en memoria y no en SQL a proposito: la mitad de los criterios
    /// comparan solo la <i>parte de fecha</i> de un <c>DateTime</c>, y eso en SQLite obliga a
    /// funciones de texto sobre la columna que ademas anulan cualquier indice. Una lista de tareas
    /// personales no llega al tamaño en que eso importe, y a cambio el criterio se escribe una vez
    /// (<see cref="TaskFilters.Matches"/>) y vale igual en Windows y en Android.</para>
    ///
    /// <para>El orden es el mismo que en las listas: primero lo que queda por hacer.</para>
    /// </remarks>
    /// <param name="search">Texto a buscar en todos los campos, o null para no filtrar.</param>
    public async Task<List<TaskItem>> GetAllTasksAsync(
        TaskFilter filter, string? tag = null, string? search = null)
    {
        var tasks = await Db.Table<TaskItem>()
                            .Where(t => !t.Deleted)
                            .ToListAsync().ConfigureAwait(false);

        var today = DateTime.Now.Date;

        tasks = FilterByTag(tasks, tag)
            .Where(t => TaskFilters.Matches(t, filter, today))
            // Las ancladas, arriba del todo y siempre; entre ellas manda el vencimiento y, sin
            // vencimiento, la mas reciente. Va por delante incluso del orden manual: anclar algo es
            // decir «esto arriba», y tener que ademas arrastrarlo hasta arriba seria decirlo dos
            // veces.
            //
            // Despues, el orden manual ANTES que el plazo: donde se puede arrastrar, lo que el
            // usuario coloca a mano tiene que quedarse donde lo puso. Con el plazo por delante,
            // arrastrar parecia funcionar y a la siguiente recarga la fila volvia a su sitio.
            .OrderBy(t => t.IsDone)
            .ThenByDescending(t => t.IsPinned)
            .ThenBy(t => t.IsPinned ? t.DueAt ?? DateTime.MaxValue : DateTime.MinValue)
            .ThenBy(t => t.SortOrder)
            .ThenBy(t => t.DueAt ?? DateTime.MaxValue)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();

        tasks = await FilterBySearchAsync(tasks, search).ConfigureAwait(false);

        await FillStepCountsAsync(tasks).ConfigureAwait(false);
        return tasks;
    }

    /// <summary>Cuantas quedan por hacer en total. Es lo que cuenta el icono de la bandeja.</summary>
    public Task<int> CountPendingAsync() =>
        Db.Table<TaskItem>().Where(t => !t.Deleted && !t.IsDone).CountAsync();

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

    /// <summary>
    /// Borra la tarea y sus pasos, de verdad. Lo que viaja al servidor es el apunte de baja
    /// (ver <see cref="DeleteListAsync"/>), no una fila marcada.
    /// </summary>
    public async Task DeleteTaskAsync(TaskItem task)
    {
        await Db.ExecuteAsync("DELETE FROM task_steps WHERE TaskId = ?", task.Id).ConfigureAwait(false);
        await Db.ExecuteAsync("DELETE FROM task_attachments WHERE TaskId = ?", task.Id).ConfigureAwait(false);
        await Db.DeleteAsync(task).ConfigureAwait(false);
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

    /// <summary>
    /// Fija el orden de los pasos a partir de como han quedado tras arrastrar.
    /// </summary>
    /// <remarks>
    /// Mismo criterio que <see cref="ReorderTasksAsync"/>: se renumera la lista entera de 0 en
    /// adelante en vez de tocar solo el que se ha movido, porque con numeros consecutivos no hay
    /// empates ni huecos que se agoten. En los pasos importa mas todavia que en las tareas: son el
    /// guion de como se hace algo, y un guion desordenado no sirve de nada.
    /// </remarks>
    // -----------------------------------------------------------------------
    // Adjuntos
    // -----------------------------------------------------------------------

    public Task<List<TaskAttachment>> GetAttachmentsAsync(Guid taskId) =>
        Db.Table<TaskAttachment>()
          .Where(a => a.TaskId == taskId && !a.Deleted)
          .OrderBy(a => a.SortOrder)
          .ToListAsync();

    public async Task<TaskAttachment?> GetAttachmentAsync(Guid id) =>
        await Db.Table<TaskAttachment>().Where(a => a.Id == id).FirstOrDefaultAsync().ConfigureAwait(false);

    /// <summary>Guarda un enlace en una tarea.</summary>
    public async Task<TaskAttachment> AddLinkAsync(Guid taskId, string url, string? name = null)
    {
        var clean = url.Trim();

        // Sin esquema, el sistema no sabe abrirlo: se asume https, que es lo que la gente pega.
        if (clean.Length > 0 && !clean.Contains("://", StringComparison.Ordinal))
        {
            clean = "https://" + clean;
        }

        var attachment = new TaskAttachment
        {
            TaskId = taskId,
            Kind = TaskAttachment.KindUrl,
            Url = clean,
            Name = string.IsNullOrWhiteSpace(name) ? clean : name.Trim(),
            SortOrder = await Db.Table<TaskAttachment>().Where(a => a.TaskId == taskId).CountAsync()
                                .ConfigureAwait(false),
        };

        await Db.InsertAsync(attachment).ConfigureAwait(false);
        await QueueAsync("task_attachments", attachment.Id, "upsert").ConfigureAwait(false);

        return attachment;
    }

    /// <summary>
    /// Guarda un fichero <b>dentro</b> de la tarea.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Si pasa de <see cref="TaskAttachment.MaxFileBytes"/>. Se avisa en vez de guardarlo a medias:
    /// un adjunto enorme no rompe esta pantalla, rompe la sincronizacion de todo lo demas.
    /// </exception>
    public async Task<TaskAttachment> AddFileAsync(Guid taskId, string fileName, byte[] data)
    {
        if (data.Length > TaskAttachment.MaxFileBytes)
        {
            throw new InvalidOperationException("El fichero es demasiado grande.");
        }

        var attachment = new TaskAttachment
        {
            TaskId = taskId,
            Kind = TaskAttachment.KindFile,
            Name = Path.GetFileName(fileName),
            Data = data,
            SortOrder = await Db.Table<TaskAttachment>().Where(a => a.TaskId == taskId).CountAsync()
                                .ConfigureAwait(false),
        };

        await Db.InsertAsync(attachment).ConfigureAwait(false);
        await QueueAsync("task_attachments", attachment.Id, "upsert").ConfigureAwait(false);

        return attachment;
    }

    public async Task DeleteAttachmentAsync(TaskAttachment attachment)
    {
        await Db.DeleteAsync(attachment).ConfigureAwait(false);
        await QueueAsync("task_attachments", attachment.Id, "delete").ConfigureAwait(false);
    }

    /// <summary>Cambia el texto de un paso. Vacio no vale: un paso sin texto no dice que hacer.</summary>
    public async Task RenameStepAsync(TaskStep step, string title)
    {
        var clean = title.Trim();
        if (clean.Length == 0 || clean == step.Title)
        {
            return;
        }

        step.Title = clean;
        step.UpdatedAt = DateTime.UtcNow;

        await Db.UpdateAsync(step).ConfigureAwait(false);
        await QueueAsync("task_steps", step.Id, "upsert").ConfigureAwait(false);
    }

    public async Task ReorderStepsAsync(IReadOnlyList<Guid> orderedIds)
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
                db.Execute("UPDATE task_steps SET SortOrder = ?, UpdatedAt = ? WHERE Id = ?",
                    i, now, orderedIds[i]);
            }
        }).ConfigureAwait(false);

        foreach (var id in orderedIds)
        {
            await QueueAsync("task_steps", id, "upsert").ConfigureAwait(false);
        }
    }

    public async Task DeleteStepAsync(TaskStep step)
    {
        await Db.DeleteAsync(step).ConfigureAwait(false);
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

    /// <summary>
    /// Se ha encolado un cambio hecho <b>aqui</b>. Es el unico sitio por el que pasan todos, asi
    /// que engancharse aqui es engancharse a cualquier cambio local sin tener que acordarse de
    /// avisar en cada metodo. Lo que baja del servidor no pasa por la cola
    /// (<see cref="SaveFromRemoteAsync"/>) y por eso no se realimenta.
    /// </summary>
    public event EventHandler? LocalChangeQueued;

    private async Task QueueAsync(string entity, Guid id, string operation)
    {
        await Db.InsertAsync(new SyncOp { Entity = entity, EntityId = id.ToString(), Operation = operation })
                .ConfigureAwait(false);

        LocalChangeQueued?.Invoke(this, EventArgs.Empty);
    }

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

    /// <summary>
    /// Mete en la cola <b>todo lo que ya hay aqui</b>, para que suba al servidor.
    /// </summary>
    /// <remarks>
    /// <para>Hace falta porque la cola solo recoge cambios <i>a partir de ahora</i>. Lo escrito
    /// antes de que hubiera cuenta —o antes de que el servidor tuviera la entrada configurada— no
    /// dejo ninguna huella en ella: sin esto, esas tareas se quedarian para siempre en el aparato
    /// donde se escribieron, que es justo lo contrario de tener una cuenta.</para>
    ///
    /// <para>Se encola como <c>upsert</c>, asi que repetirlo no duplica nada: si la fila ya esta
    /// arriba se pisa con la misma, y manda igualmente la fecha mas reciente.</para>
    /// </remarks>
    public async Task<int> QueueEverythingAsync()
    {
        var lists = await Db.Table<TaskList>().ToListAsync().ConfigureAwait(false);
        var tasks = await Db.Table<TaskItem>().ToListAsync().ConfigureAwait(false);
        var steps = await Db.Table<TaskStep>().ToListAsync().ConfigureAwait(false);
        var attachments = await Db.Table<TaskAttachment>().ToListAsync().ConfigureAwait(false);

        // Las listas primero: una tarea cuya lista aun no existe arriba no tendria donde colgarse.
        var ops = new List<SyncOp>(lists.Count + tasks.Count + steps.Count);
        ops.AddRange(lists.Select(l => new SyncOp { Entity = "task_lists", EntityId = l.Id.ToString(), Operation = "upsert" }));
        ops.AddRange(tasks.Select(t => new SyncOp { Entity = "tasks", EntityId = t.Id.ToString(), Operation = "upsert" }));
        ops.AddRange(steps.Select(s => new SyncOp { Entity = "task_steps", EntityId = s.Id.ToString(), Operation = "upsert" }));
        ops.AddRange(attachments.Select(a => new SyncOp { Entity = "task_attachments", EntityId = a.Id.ToString(), Operation = "upsert" }));

        if (ops.Count > 0)
        {
            await Db.InsertAllAsync(ops).ConfigureAwait(false);
            LocalChangeQueued?.Invoke(this, EventArgs.Empty);
        }

        return ops.Count;
    }

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
