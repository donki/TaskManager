using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskManager.Core.Services;

/// <summary>Elemento de trabajo tal y como se muestra antes de decidir si se importa.</summary>
public sealed record WorkItem(
    int Id,
    string Title,
    string Type,
    string State,
    string Project,
    string Organization,
    DateTime? DueDate,
    string Url)
{
    /// <summary>Titulo que acaba teniendo la tarea, con el numero delante para poder rastrearla.</summary>
    public string TaskTitle => $"#{Id} · {Title}";
}

/// <summary>
/// Trae de Azure DevOps los elementos de trabajo asignados a quien usa la aplicacion.
/// </summary>
/// <remarks>
/// <para><b>Sin pedir credenciales.</b> Se entra con la cuenta de Microsoft por el navegador del
/// sistema, igual que el correo, y Azure DevOps acepta ese mismo token. No hay que crear ningun
/// token personal ni pegarlo en ninguna casilla, que es justo lo que la constitucion no quiere.</para>
///
/// <para><b>Ni la organizacion se pregunta.</b> Se descubren solas a partir del perfil: pedirle a
/// alguien que escriba el nombre exacto de su organizacion de DevOps es una forma segura de que se
/// equivoque y no entienda por que no aparece nada.</para>
///
/// <para><b>Solo lectura y solo lo abierto.</b> Se traen los elementos asignados que no esten
/// cerrados ni eliminados. Nada se escribe de vuelta: la tarea que se crea aqui es <i>tuya</i>, y
/// completarla no cierra el elemento en DevOps —eso lo decide quien lleve el tablero, no esta
/// aplicacion.</para>
/// </remarks>
public sealed class AzureDevOpsService
{
    private const string ProfileUrl =
        "https://app.vssps.visualstudio.com/_apis/profile/profiles/me?api-version=7.1";

    private const string AccountsUrl =
        "https://app.vssps.visualstudio.com/_apis/accounts?memberId={0}&api-version=7.1";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly MailOAuthService _oauth;

    public AzureDevOpsService(HttpClient http, MailOAuthService oauth)
    {
        _http = http;
        _oauth = oauth;
    }

    public static bool IsConfigured => MailOAuthConfig.IsConfigured(MailOAuthProvider.AzureDevOps);

    /// <summary>Entra con la cuenta de Microsoft. Abre el navegador del sistema.</summary>
    public Task<MailOAuthSession> SignInAsync(CancellationToken cancellationToken = default) =>
        _oauth.SignInAsync(MailOAuthProvider.AzureDevOps, cancellationToken);

    /// <summary>Recupera la sesion guardada, o <c>null</c> si no hay o ya no vale.</summary>
    public Task<MailOAuthSession?> RestoreAsync(CancellationToken cancellationToken = default) =>
        _oauth.RestoreAsync(MailOAuthProvider.AzureDevOps, cancellationToken);

    public Task SignOutAsync() => _oauth.SignOutAsync(MailOAuthProvider.AzureDevOps);

    // -----------------------------------------------------------------------

    /// <summary>
    /// Elementos asignados al usuario en todas sus organizaciones.
    /// </summary>
    /// <remarks>
    /// Si una organizacion falla se salta y se sigue con las demas: es normal tener acceso a varias
    /// y que en alguna no haya proyectos o falte permiso, y no tendria sentido que eso dejara la
    /// pantalla vacia del todo.
    /// </remarks>
    public async Task<List<WorkItem>> GetAssignedAsync(string token,
        CancellationToken cancellationToken = default)
    {
        var items = new List<WorkItem>();

        foreach (var organization in await GetOrganizationsAsync(token, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                items.AddRange(await GetAssignedInAsync(organization, token, cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (Exception)
            {
                // Organizacion sin proyectos, sin permiso o caida: se sigue con la siguiente.
            }
        }

        return items;
    }

    private async Task<List<string>> GetOrganizationsAsync(string token,
        CancellationToken cancellationToken)
    {
        var profile = await GetAsync<Profile>(ProfileUrl, token, cancellationToken).ConfigureAwait(false);
        if (profile?.Id is not { Length: > 0 } memberId)
        {
            return [];
        }

        var accounts = await GetAsync<Wrapper<Account>>(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, AccountsUrl, memberId),
            token, cancellationToken).ConfigureAwait(false);

        return accounts?.Value?.Select(a => a.AccountName).Where(a => a.Length > 0).ToList() ?? [];
    }

    private async Task<List<WorkItem>> GetAssignedInAsync(string organization, string token,
        CancellationToken cancellationToken)
    {
        // WIQL, que es el lenguaje de consulta de los tableros. @Me lo resuelve el servidor con el
        // usuario del token, asi que no hay que saber aqui como se llama nadie.
        const string wiql =
            "SELECT [System.Id] FROM WorkItems " +
            "WHERE [System.AssignedTo] = @Me " +
            "AND [System.State] NOT IN ('Closed', 'Done', 'Removed', 'Resolved') " +
            "ORDER BY [System.ChangedDate] DESC";

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://dev.azure.com/{organization}/_apis/wit/wiql?api-version=7.1&$top=100")
        {
            Content = JsonContent.Create(new { query = wiql }),
        };

        Authorize(request, token);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var result = await response.Content
            .ReadFromJsonAsync<WiqlResult>(Json, cancellationToken).ConfigureAwait(false);

        var ids = result?.WorkItems?.Select(w => w.Id).ToList() ?? [];
        if (ids.Count == 0)
        {
            return [];
        }

        return await GetDetailsAsync(organization, ids, token, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<WorkItem>> GetDetailsAsync(string organization, List<int> ids,
        string token, CancellationToken cancellationToken)
    {
        var items = new List<WorkItem>();

        // La API acepta 200 identificadores por peticion. Se trocea por si acaso: pasarse devuelve
        // un 400 y perderiamos la tanda entera, no solo lo que sobra.
        foreach (var batch in ids.Chunk(200))
        {
            var url = $"https://dev.azure.com/{organization}/_apis/wit/workitems" +
                      $"?ids={string.Join(',', batch)}" +
                      "&fields=System.Id,System.Title,System.WorkItemType,System.State,System.TeamProject," +
                      "Microsoft.VSTS.Scheduling.DueDate&api-version=7.1";

            var page = await GetAsync<Wrapper<RawWorkItem>>(url, token, cancellationToken).ConfigureAwait(false);

            foreach (var raw in page?.Value ?? [])
            {
                var f = raw.Fields;
                items.Add(new WorkItem(
                    raw.Id,
                    f.Title ?? string.Empty,
                    f.WorkItemType ?? string.Empty,
                    f.State ?? string.Empty,
                    f.TeamProject ?? string.Empty,
                    organization,
                    f.DueDate,
                    $"https://dev.azure.com/{organization}/_workitems/edit/{raw.Id}"));
            }
        }

        return items;
    }

    // -----------------------------------------------------------------------

    private async Task<T?> GetAsync<T>(string url, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        Authorize(request, token);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false);
    }

    private static void Authorize(HttpRequestMessage request, string token) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    // Respuestas del servidor, con solo lo que se usa.
    private sealed record Profile(string Id);

    private sealed record Wrapper<T>(List<T>? Value);

    private sealed record Account([property: JsonPropertyName("accountName")] string AccountName);

    private sealed record WiqlResult(List<WiqlId>? WorkItems);

    private sealed record WiqlId(int Id);

    private sealed record RawWorkItem(int Id, Fields Fields);

    private sealed record Fields(
        [property: JsonPropertyName("System.Title")] string? Title,
        [property: JsonPropertyName("System.WorkItemType")] string? WorkItemType,
        [property: JsonPropertyName("System.State")] string? State,
        [property: JsonPropertyName("System.TeamProject")] string? TeamProject,
        [property: JsonPropertyName("Microsoft.VSTS.Scheduling.DueDate")] DateTime? DueDate);
}
