using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TaskManager.Core.Data;
using TaskManager.Core.Models;

namespace TaskManager.Core.Services;

/// <summary>
/// Sincronizacion real contra Supabase por PostgREST.
/// </summary>
/// <remarks>
/// <para><b>Para que existe.</b> Es lo que hace que la aplicacion de Windows y la de Android sean
/// la misma aplicacion y no dos cuadernos sueltos: las tareas se suben con la cuenta del usuario y
/// bajan en el otro dispositivo. Sin sesion iniciada esto no puede funcionar —una sesion anonima da
/// un usuario distinto en cada aparato, que es justo lo contrario de compartir—, asi que el
/// servicio no hace nada hasta que hay un token de verdad.</para>
///
/// <para><b>Como resuelve los conflictos.</b> Gana la escritura mas reciente por
/// <c>updated_at</c>. Es la regla mas simple que se comporta bien con una lista de tareas: dos
/// dispositivos rara vez tocan la misma tarea a la vez, y cuando pasa, lo ultimo que hizo el
/// usuario es casi siempre lo que queria. No se intenta fusionar campo a campo: seria mas listo en
/// teoria y mucho mas dificil de predecir en la practica.</para>
///
/// <para><b>Por que no se borra nada de verdad.</b> Un borrado viaja como <c>deleted = true</c>. Si
/// se borrase la fila, el dispositivo que estuviera desconectado no se enteraria nunca —no hay
/// forma de sincronizar una ausencia— y la tarea reapareceria al volver.</para>
/// </remarks>
public sealed class SupabaseSyncService : ISyncService
{
    /// <summary>
    /// Ultima bajada correcta, para pedir solo lo que ha llegado al servidor desde entonces. Se
    /// compara contra <c>synced_at</c>, no contra <c>updated_at</c> (ver <see cref="FetchAsync"/>).
    /// </summary>
    private const string KeyLastPull = "sync.last_pull_v2";

    /// <remarks>
    /// <para><b>Los nulos se escriben.</b> Antes se omitian (<c>WhenWritingNull</c>) y eso rompia la
    /// subida en cuanto habia mas de una fila: una tarea sin plazo se serializaba sin
    /// <c>due_at</c> y otra con plazo si lo llevaba, asi que el lote iba con objetos de distinta
    /// forma y PostgREST lo rechazaba entero con
    /// <c>PGRST102: All object keys must match</c>. Se veia solo con varias tareas —con una sola,
    /// todas las claves coinciden por definicion—, que es justo el caso que no se prueba.</para>
    ///
    /// <para>Ademas es lo correcto para un <c>upsert</c>: quitarle el plazo a una tarea tiene que
    /// llegar al servidor como <c>due_at = null</c>, no como «de este campo no digo nada».</para>
    /// </remarks>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;
    private readonly TaskRepository _repository;
    private readonly SettingsService _settings;
    private readonly SupabaseAuthService _auth;

    public SupabaseSyncService(HttpClient http, TaskRepository repository,
        SettingsService settings, SupabaseAuthService auth)
    {
        _http = http;
        _repository = repository;
        _settings = settings;
        _auth = auth;
    }

    public bool IsConfigured => SupabaseConfig.IsConfigured;

    public event EventHandler<RemoteChange>? RemoteChanged;

    /// <summary>Sube lo pendiente y baja lo nuevo. Es lo que se llama al abrir y al refrescar.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await PushAsync(cancellationToken).ConfigureAwait(false);
        await PullAsync(cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Subida
    // -----------------------------------------------------------------------

    public async Task<int> PushAsync(CancellationToken cancellationToken = default)
    {
        var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return 0;
        }

        var pending = await _repository.GetPendingSyncAsync().ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return 0;
        }

        // Una misma tarea puede haberse tocado diez veces sin conexion. Solo interesa como ha
        // quedado, asi que de cada entidad se sube su estado actual una unica vez.
        var latest = pending
            .GroupBy(op => (op.Entity, op.EntityId))
            .Select(g => g.Last())
            .ToList();

        var done = new List<SyncOp>();

        // Las bajas van por su camino: borran la fila de verdad y dejan un apunte en `deletions`,
        // que es lo unico que puede contarle al otro dispositivo que aquello ya no esta.
        foreach (var op in latest.Where(o => o.Operation == "delete"))
        {
            if (!Guid.TryParse(op.EntityId, out var id))
            {
                continue;
            }

            if (await DeleteRemoteAsync(op.Entity, id, token, cancellationToken).ConfigureAwait(false))
            {
                done.AddRange(pending.Where(o => o.Entity == op.Entity && o.EntityId == op.EntityId));
            }
        }

        foreach (var group in latest.Where(o => o.Operation != "delete").GroupBy(op => op.Entity))
        {
            var rows = new List<object>();
            var sent = new List<SyncOp>();

            foreach (var op in group)
            {
                var row = await BuildRowAsync(group.Key, op.EntityId).ConfigureAwait(false);
                if (row is not null)
                {
                    rows.Add(row);
                    sent.Add(op);
                }
            }

            if (rows.Count == 0)
            {
                continue;
            }

            if (await UpsertAsync(group.Key, rows, token, cancellationToken).ConfigureAwait(false))
            {
                // Solo se da por hecho lo que de verdad se ha mandado. Antes se descartaba TODO lo
                // pendiente de esa entidad, incluidas las filas que no se pudieron construir: se
                // perdian sin haber subido nunca.
                done.AddRange(sent);
            }
        }

        // Solo se descarta de la cola lo que el servidor ha aceptado. Si algo falla se reintenta
        // en la siguiente vuelta en vez de perderse en silencio.
        await _repository.ClearSyncAsync(done).ConfigureAwait(false);
        return done.Count;
    }

    private async Task<object?> BuildRowAsync(string entity, string entityId)
    {
        if (!Guid.TryParse(entityId, out var id))
        {
            return null;
        }

        return entity switch
        {
            "tasks" => await _repository.GetTaskAsync(id).ConfigureAwait(false) is { } t ? ToRow(t) : null,
            "task_lists" => await _repository.GetListAsync(id).ConfigureAwait(false) is { } l ? ToRow(l) : null,
            "task_steps" => await _repository.GetStepAsync(id).ConfigureAwait(false) is { } s ? ToRow(s) : null,
            "task_attachments" => await _repository.GetAttachmentAsync(id).ConfigureAwait(false) is { } a ? ToRow(a) : null,
            _ => null,
        };
    }

    private async Task<bool> UpsertAsync(string table, IReadOnlyList<object> rows, string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseConfig.Url}/rest/v1/{table}")
        {
            Content = JsonContent.Create(rows, options: Json),
        };

        Authorize(request, token);

        // merge-duplicates = upsert: la primera vez inserta y a partir de ahi actualiza, sin tener
        // que saber desde el cliente si la fila ya existe alli.
        request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            // Antes solo se miraba el codigo de estado, y un rechazo del servidor era
            // indistinguible de no tener red: la fila se quedaba en la cola reintentandose para
            // siempre sin que nadie supiera por que. Un 400 de PostgREST dice exactamente que
            // columna sobra o falta, y esa frase es la diferencia entre un minuto y una tarde.
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            LastError = $"{table}: {(int)response.StatusCode} {body}";
            System.Diagnostics.Debug.WriteLine($"Sync push rechazado — {LastError}");

            return false;
        }
        catch (HttpRequestException)
        {
            return false;   // Sin red: la cola espera y se reintenta. No es un error que contar.
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>Ultimo rechazo del servidor, para poder enseñarlo. Null si no ha habido ninguno.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Borra la fila del servidor y deja constancia de la baja.
    /// </summary>
    /// <remarks>
    /// <para><b>El apunte primero.</b> Si se borrase la fila y luego fallara el apunte, la fila ya
    /// no estaria y nadie podria enterarse: el otro dispositivo se quedaria con ella para siempre.
    /// Al reves, un apunte sin borrar solo hace que el otro borre algo que aqui todavia esta, y la
    /// siguiente vuelta lo arregla.</para>
    ///
    /// <para>Las filas que cuelgan —tareas de una lista, pasos de una tarea— las borra el propio
    /// servidor por las claves ajenas; aqui basta con la de arriba.</para>
    /// </remarks>
    private async Task<bool> DeleteRemoteAsync(string entity, Guid id, string token,
        CancellationToken cancellationToken)
    {
        var noted = await UpsertAsync(
            "deletions",
            [new { entity, entity_id = id }],
            token,
            cancellationToken).ConfigureAwait(false);

        if (!noted)
        {
            return false;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Delete, $"{SupabaseConfig.Url}/rest/v1/{entity}?id=eq.{id}");

        Authorize(request, token);
        request.Headers.Add("Prefer", "return=minimal");

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            LastError = $"{entity} (borrado): {(int)response.StatusCode} " +
                        await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"Sync borrado rechazado - {LastError}");

            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Bajada
    // -----------------------------------------------------------------------

    public async Task PullAsync(CancellationToken cancellationToken = default)
    {
        var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return;
        }

        var since = _settings.Get(KeyLastPull, string.Empty);

        // El corte se toma ANTES de pedir nada. Si se tomara despues, todo lo que cambie mientras
        // dura la peticion caeria en el hueco entre las dos bajadas y no llegaria nunca.
        var cutoff = DateTime.UtcNow;

        // Las bajas primero: si llegara antes una tarea que el borrado de su lista, se guardaria
        // una tarea huerfana que luego habria que volver a limpiar.
        var deletions = await FetchAsync<DeletionRow>("deletions", since, token, cancellationToken, "deleted_at")
            .ConfigureAwait(false);

        var lists = await FetchAsync<ListRow>("task_lists", since, token, cancellationToken).ConfigureAwait(false);
        var tasks = await FetchAsync<TaskRowDto>("tasks", since, token, cancellationToken).ConfigureAwait(false);
        var steps = await FetchAsync<StepRow>("task_steps", since, token, cancellationToken).ConfigureAwait(false);
        var attachments = await FetchAsync<AttachmentRow>("task_attachments", since, token, cancellationToken)
            .ConfigureAwait(false);

        if (deletions is null || lists is null || tasks is null || steps is null || attachments is null)
        {
            return;   // Fallo de red: no se mueve el corte, se reintenta entero la proxima vez.
        }

        foreach (var row in deletions)
        {
            await _repository.ApplyRemoteDeleteAsync(row.Entity, row.EntityId).ConfigureAwait(false);
            RemoteChanged?.Invoke(this, new RemoteChange(row.Entity, row.EntityId.ToString()));
        }

        // Las listas primero: una tarea cuya lista aun no existe se quedaria huerfana en la interfaz.
        foreach (var row in lists)
        {
            await MergeListAsync(row).ConfigureAwait(false);
        }

        foreach (var row in tasks)
        {
            await MergeTaskAsync(row).ConfigureAwait(false);
        }

        foreach (var row in steps)
        {
            await MergeStepAsync(row).ConfigureAwait(false);
        }

        foreach (var row in attachments)
        {
            await MergeAttachmentAsync(row).ConfigureAwait(false);
        }

        await _settings.SetAsync(KeyLastPull, cutoff.ToString("O", CultureInfo.InvariantCulture))
                       .ConfigureAwait(false);
    }

    private async Task<List<T>?> FetchAsync<T>(string table, string since, string token,
        CancellationToken cancellationToken, string sinceColumn = "synced_at")
    {
        // Se pregunta por synced_at —cuando llego la fila al servidor—, no por updated_at, que es
        // cuando la toco el usuario. Con updated_at, lo que un dispositivo sube por primera vez
        // viaja con su fecha original (de hace dias) y cae por detras del corte del otro
        // dispositivo: se guardaba en el servidor y el otro no lo veia nunca.
        var url = $"{SupabaseConfig.Url}/rest/v1/{table}?select=*";
        if (since.Length > 0)
        {
            url += $"&{sinceColumn}=gt.{Uri.EscapeDataString(since)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        Authorize(request, token);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content
                .ReadFromJsonAsync<List<T>>(Json, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private static void Authorize(HttpRequestMessage request, string token)
    {
        request.Headers.Add("apikey", SupabaseConfig.PublishableKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    // -----------------------------------------------------------------------
    // Fusion: gana lo mas reciente
    // -----------------------------------------------------------------------

    private async Task MergeTaskAsync(TaskRowDto row)
    {
        var local = await _repository.GetTaskAsync(row.Id).ConfigureAwait(false);
        if (local is not null && local.UpdatedAt >= row.UpdatedAt)
        {
            return;   // Lo de aqui es mas nuevo: ya lo subira el push, no se pisa.
        }

        var task = local ?? new TaskItem { Id = row.Id };

        task.ListId = row.ListId;
        task.Title = row.Title;
        task.Notes = row.Notes;
        task.IsPriority = row.IsPriority;
        task.IsDone = row.IsDone;
        task.DoneAt = row.DoneAt?.UtcDateTime;
        task.MyDayOn = row.MyDayOn?.Date;
        task.DueAt = row.DueAt?.UtcDateTime;
        task.PlannedFor = row.PlannedFor?.Date;
        task.Tags = row.Tags;
        task.RecurrenceRule = row.RecurrenceRule;
        task.SortOrder = row.SortOrder;
        task.UpdatedAt = row.UpdatedAt;
        task.Deleted = row.Deleted;

        await _repository.SaveFromRemoteAsync(task, local is null).ConfigureAwait(false);
        RemoteChanged?.Invoke(this, new RemoteChange("tasks", row.Id.ToString()));
    }

    private async Task MergeListAsync(ListRow row)
    {
        var local = await _repository.GetListAsync(row.Id).ConfigureAwait(false);
        if (local is not null && local.UpdatedAt >= row.UpdatedAt)
        {
            return;
        }

        var list = local ?? new TaskList { Id = row.Id };

        list.GroupId = row.GroupId;
        list.OwnerId = row.OwnerId?.ToString() ?? string.Empty;
        list.Name = row.Name;
        list.Icon = row.Icon;
        list.SortOrder = row.SortOrder;
        list.UpdatedAt = row.UpdatedAt;
        list.Deleted = row.Deleted;

        await _repository.SaveFromRemoteAsync(list, local is null).ConfigureAwait(false);
    }

    private async Task MergeStepAsync(StepRow row)
    {
        var local = await _repository.GetStepAsync(row.Id).ConfigureAwait(false);
        if (local is not null && local.UpdatedAt >= row.UpdatedAt)
        {
            return;
        }

        var step = local ?? new TaskStep { Id = row.Id };

        step.TaskId = row.TaskId;
        step.Title = row.Title;
        step.IsDone = row.IsDone;
        step.SortOrder = row.SortOrder;
        step.Source = row.Source;
        step.UpdatedAt = row.UpdatedAt;
        step.Deleted = row.Deleted;

        await _repository.SaveFromRemoteAsync(step, local is null).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Grupos
    // -----------------------------------------------------------------------

    public async Task<string> CreateGroupAsync(string name, string sharedKey,
        CancellationToken cancellationToken = default)
    {
        var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new AuthException("Hay que entrar con una cuenta para crear un grupo.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{SupabaseConfig.Url}/rest/v1/rpc/create_group")
        {
            Content = JsonContent.Create(new { p_name = name, p_key = sharedKey }, options: Json),
        };

        Authorize(request, token);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var code = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return code.Trim('"', ' ', '\n', '\r');
    }

    public async Task<Guid> JoinGroupAsync(string joinCode, string sharedKey,
        CancellationToken cancellationToken = default)
    {
        var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new AuthException("Hay que entrar con una cuenta para unirse a un grupo.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{SupabaseConfig.Url}/rest/v1/rpc/join_group")
        {
            Content = JsonContent.Create(new { p_code = joinCode, p_key = sharedKey }, options: Json),
        };

        Authorize(request, token);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Guid.TryParse(body.Trim('"', ' ', '\n', '\r'), out var id)
            ? id
            : throw new AuthException("El codigo o la clave del grupo no son correctos.");
    }

    // -----------------------------------------------------------------------
    // Correspondencia con las columnas del servidor
    // -----------------------------------------------------------------------

    private object ToRow(TaskItem t) => new
    {
        id = t.Id,
        list_id = t.ListId,
        title = t.Title,
        notes = t.Notes,
        is_priority = t.IsPriority,
        is_done = t.IsDone,
        done_at = t.DoneAt,
        my_day_on = t.MyDayOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        due_at = t.DueAt,
        planned_for = t.PlannedFor?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        tags = t.Tags,
        recurrence_rule = t.RecurrenceRule,
        sort_order = t.SortOrder,
        breakdown_rewarded = t.BreakdownRewarded,
        created_by = CurrentUserId(),
        created_at = t.CreatedAt,
        updated_at = t.UpdatedAt,
        deleted = t.Deleted,
    };

    /// <remarks>
    /// Los bytes viajan como texto hexadecimal con prefijo <c>\x</c>, que es la representacion que
    /// PostgREST entiende para un <c>bytea</c> sin conversiones raras. Un enlace no lleva datos y
    /// viaja como <c>null</c>.
    /// </remarks>
    private object ToRow(TaskAttachment a) => new
    {
        id = a.Id,
        task_id = a.TaskId,
        kind = a.Kind,
        name = a.Name,
        url = a.Url,
        data = a.Data is { Length: > 0 } bytes ? "\\x" + Convert.ToHexString(bytes).ToLowerInvariant() : null,
        sort_order = a.SortOrder,
        updated_at = a.UpdatedAt,
        deleted = a.Deleted,
    };

    private object ToRow(TaskList l) => new
    {
        id = l.Id,
        group_id = l.GroupId,
        owner_id = CurrentUserId(),
        name = l.Name,
        icon = l.Icon,
        sort_order = l.SortOrder,
        updated_at = l.UpdatedAt,
        deleted = l.Deleted,
    };

    private static object ToRow(TaskStep s) => new
    {
        id = s.Id,
        task_id = s.TaskId,
        title = s.Title,
        is_done = s.IsDone,
        sort_order = s.SortOrder,
        source = s.Source,
        updated_at = s.UpdatedAt,
        deleted = s.Deleted,
    };

    /// <summary>
    /// Identificador del usuario <b>en el servidor</b>. Las columnas de autoria apuntan a
    /// <c>auth.users</c>, asi que tiene que ser el <c>auth.uid()</c> del token, no el de Google:
    /// dentro de la aplicacion el usuario es el <c>sub</c> de Google, pero la RLS no sabe nada de
    /// el y rechazaria la fila.
    /// </summary>
    private string CurrentUserId()
    {
        var remote = _auth.CurrentUser?.RemoteId ?? string.Empty;
        return remote.Length > 0 ? remote : _settings.Get(SettingsService.KeyRemoteUserId, string.Empty);
    }

    /// <summary>
    /// Escribe un adjunto que viene del servidor.
    /// </summary>
    /// <remarks>
    /// El <c>bytea</c> llega como texto hexadecimal con prefijo <c>\x</c>, que es como lo devuelve
    /// PostgREST; si algun dia cambiara de forma, se prefiere quedarse sin los bytes a reventar la
    /// bajada entera de las demas filas.
    /// </remarks>
    private async Task MergeAttachmentAsync(AttachmentRow row)
    {
        var local = await _repository.GetAttachmentAsync(row.Id).ConfigureAwait(false);
        if (local is not null && local.UpdatedAt >= row.UpdatedAt)
        {
            return;
        }

        var attachment = local ?? new TaskAttachment { Id = row.Id };

        attachment.TaskId = row.TaskId;
        attachment.Kind = row.Kind;
        attachment.Name = row.Name;
        attachment.Url = row.Url;
        attachment.SortOrder = row.SortOrder;
        attachment.UpdatedAt = row.UpdatedAt.UtcDateTime;
        attachment.Deleted = row.Deleted;
        attachment.Data = FromHex(row.Data);

        await _repository.SaveFromRemoteAsync(attachment, local is null).ConfigureAwait(false);
        RemoteChanged?.Invoke(this, new RemoteChange("task_attachments", row.Id.ToString()));
    }

    private static byte[]? FromHex(string? value)
    {
        if (value is null || !value.StartsWith("\\x", StringComparison.Ordinal) || value.Length < 4)
        {
            return null;
        }

        try
        {
            return Convert.FromHexString(value[2..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Un adjunto tal y como lo devuelve PostgREST.</summary>
    private sealed record AttachmentRow(
        Guid Id, Guid TaskId, string Kind, string Name, string Url, string? Data,
        int SortOrder, DateTimeOffset UpdatedAt, bool Deleted);

    /// <summary>Un apunte de baja: que se borro y cuando llego aqui la noticia.</summary>
    private sealed record DeletionRow(string Entity, Guid EntityId, DateTimeOffset DeletedAt);

    // Filas tal y como las devuelve PostgREST. Son un tipo aparte a proposito: si el servidor
    // cambia, se ve aqui y no se cuela dentro del modelo de la aplicacion.
    private sealed record TaskRowDto(
        Guid Id, Guid ListId, string Title, string Notes, bool IsPriority, bool IsDone,
        DateTimeOffset? DoneAt, DateTimeOffset? MyDayOn, DateTimeOffset? DueAt, DateTimeOffset? PlannedFor,
        string Tags, string RecurrenceRule, int SortOrder, DateTime UpdatedAt, bool Deleted);

    private sealed record ListRow(
        Guid Id, Guid? GroupId, Guid? OwnerId, string Name, string Icon, int SortOrder,
        DateTime UpdatedAt, bool Deleted);

    private sealed record StepRow(
        Guid Id, Guid TaskId, string Title, bool IsDone, int SortOrder, string Source,
        DateTime UpdatedAt, bool Deleted);
}
