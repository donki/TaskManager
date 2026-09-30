using System.Net;
using System.Text;
using System.Text.Json;
using TaskManager.Core.Services;

namespace TaskManager.Tests;

/// <summary>
/// Un servidor de mentira: responde a las peticiones de la aplicacion sin salir del proceso. Nada
/// de lo que prueba esto toca Supabase, Google ni Microsoft de verdad.
/// </summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    public sealed record Call(HttpMethod Method, string Url, string Body, HttpRequestMessage Request);

    private readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = [];

    public List<Call> Calls { get; } = [];

    /// <summary>Lo que no encaja con ninguna ruta. Por defecto, un 404.</summary>
    public HttpStatusCode Fallback { get; set; } = HttpStatusCode.NotFound;

    public HttpClient Client() => new(this);

    /// <summary>Añade una ruta. Las ultimas mandan sobre las primeras.</summary>
    public FakeHttp On(HttpMethod method, string urlContains, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Insert(0, (r => r.Method == method && r.RequestUri!.ToString().Contains(urlContains, StringComparison.Ordinal), respond));
        return this;
    }

    public FakeHttp On(HttpMethod method, string urlContains, HttpStatusCode status, string body = "") =>
        On(method, urlContains, _ => Response(status, body));

    public FakeHttp OnJson(HttpMethod method, string urlContains, object payload) =>
        On(method, urlContains, _ => Response(HttpStatusCode.OK, JsonSerializer.Serialize(payload)));

    public FakeHttp Throw(HttpMethod method, string urlContains, Exception? ex = null) =>
        On(method, urlContains, _ => throw (ex ?? new HttpRequestException("sin red")));

    public static HttpResponseMessage Response(HttpStatusCode status, string body = "") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public IEnumerable<Call> To(string urlContains) => Calls.Where(c => c.Url.Contains(urlContains, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Calls)
        {
            Calls.Add(new Call(request.Method, request.RequestUri!.ToString(), body, request));
        }

        foreach (var (match, respond) in _routes)
        {
            if (match(request))
            {
                return respond(request);
            }
        }

        return Response(Fallback);
    }
}

internal sealed class FakeTokens : ITokenStore
{
    public Dictionary<string, string?> Values { get; } = [];

    public Task<string?> GetAsync(string key) => Task.FromResult(Values.GetValueOrDefault(key));

    public Task SetAsync(string key, string? value)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }
}

internal sealed class FakeBrowser(string redirect = "http://127.0.0.1:5123/auth/") : IOAuthBrowser
{
    public string RedirectUri { get; } = redirect;

    public Uri? LastAuthorize { get; private set; }

    /// <summary>Lo que devuelve el «navegador»: la direccion de vuelta, con su codigo o su error.</summary>
    public string Callback { get; set; } = "http://127.0.0.1:5123/auth/?code=codigo-1&state=x";

    public Task<Uri> AuthenticateAsync(Uri authorizeUrl, CancellationToken cancellationToken = default)
    {
        LastAuthorize = authorizeUrl;
        return Task.FromResult(new Uri(Callback));
    }
}

internal static class Jwt
{
    /// <summary>Un id_token sin firma de verdad: la aplicacion solo lee su contenido.</summary>
    public static string Make(object claims)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"none\"}")}.{B64(JsonSerializer.Serialize(claims))}.firma";
    }
}
