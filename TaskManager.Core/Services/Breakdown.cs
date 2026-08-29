using System.Text;
using System.Text.Json;

namespace TaskManager.Core.Services;

/// <summary>
/// "Pasos Magicos": convierte un objetivo amplio en 3-5 micro-pasos de 5 a 10 minutos.
/// </summary>
public interface IBreakdownService
{
    /// <summary>Nombre visible de la via usada, para poder decirle al usuario de donde salio.</summary>
    string Source { get; }

    Task<IReadOnlyList<string>> BreakdownAsync(string goal, CancellationToken cancellationToken = default);
}

/// <summary>
/// Intenta el modelo local y, si no contesta o no esta levantado, cae al desglose heuristico.
/// El usuario nunca se queda sin pasos: es la diferencia entre una funcion util y una que "a veces".
/// </summary>
public sealed class CascadingBreakdownService : IBreakdownService
{
    private readonly IReadOnlyList<IBreakdownService> _chain;

    public CascadingBreakdownService(params IBreakdownService[] chain) => _chain = chain;

    public string Source { get; private set; } = string.Empty;

    public async Task<IReadOnlyList<string>> BreakdownAsync(string goal, CancellationToken cancellationToken = default)
    {
        foreach (var service in _chain)
        {
            try
            {
                var steps = await service.BreakdownAsync(goal, cancellationToken).ConfigureAwait(false);
                if (steps.Count > 0)
                {
                    Source = service.Source;
                    return steps;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Una via caida no es un error de la aplicacion: se prueba la siguiente.
            }
        }

        Source = string.Empty;
        return [];
    }
}

/// <summary>
/// Modelo de lenguaje **local**, hablando la API de OpenAI (Ollama, llama.cpp --server, LM Studio).
/// Recomendado: Qwen2.5 3B Instruct (Apache-2.0, compatible con la regla MIT/monetizable).
/// En Android la direccion puede apuntar al PC de la LAN; ver ARQUITECTURA.md seccion 5.
/// </summary>
public sealed class LocalLlmBreakdownService : IBreakdownService
{
    private const string SystemPrompt =
        "Eres un asistente que descompone objetivos en micro-tareas. " +
        "Responde SOLO con un array JSON de 3 a 5 cadenas, en espanol, sin numerar. " +
        "Cada cadena es una accion concreta de 5 a 10 minutos que se pueda empezar ya.";

    private readonly HttpClient _http;
    private readonly Func<string> _endpoint;
    private readonly Func<string> _model;

    public LocalLlmBreakdownService(HttpClient http, Func<string> endpoint, Func<string> model)
    {
        _http = http;
        _endpoint = endpoint;
        _model = model;
    }

    public string Source => "IA local";

    public async Task<IReadOnlyList<string>> BreakdownAsync(string goal, CancellationToken cancellationToken = default)
    {
        var baseUrl = _endpoint().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return [];
        }

        var payload = new
        {
            model = _model(),
            temperature = 0.4,
            stream = false,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = goal },
            },
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"{baseUrl}/v1/chat/completions", content, cancellationToken)
                                        .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        var text = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        return ParseSteps(text);
    }

    /// <summary>
    /// El modelo local a veces envuelve el JSON en texto o en un bloque de codigo, asi que se busca
    /// el array y, si no aparece, se aprovechan las lineas sueltas.
    /// </summary>
    internal static IReadOnlyList<string> ParseSteps(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            try
            {
                var items = JsonSerializer.Deserialize<List<string>>(text[start..(end + 1)]);
                if (items is { Count: > 0 })
                {
                    return Clean(items);
                }
            }
            catch (JsonException)
            {
                // Cae al reparto por lineas.
            }
        }

        return Clean(text.Split('\n'));
    }

    private static List<string> Clean(IEnumerable<string> lines) =>
        lines.Select(l => l.Trim().Trim('-', '*', '"', ' '))
             .Select(l => l.Length > 2 && char.IsDigit(l[0]) && (l[1] == '.' || l[1] == ')') ? l[2..].Trim() : l)
             .Where(l => l.Length > 2)
             .Take(5)
             .ToList();
}

/// <summary>
/// Desglose por plantillas. Sin red, sin modelo y sin latencia: es lo que garantiza que el boton
/// de la varita responda siempre, tambien en un movil sin conexion.
/// </summary>
public sealed class HeuristicBreakdownService : IBreakdownService
{
    private static readonly (string[] Keywords, string[] Steps)[] Templates =
    [
        (["mudanza", "mudar", "piso nuevo", "casa nueva"],
         ["Conseguir cajas de carton y cinta", "Clasificar la ropa por temporada",
          "Etiquetar las cajas fragiles", "Pedir presupuesto a dos empresas de mudanzas",
          "Avisar del cambio de direccion"]),

        (["compra", "supermercado", "super", "mercado"],
         ["Revisar la nevera y la despensa", "Anotar lo que falta de la semana",
          "Ordenar la lista por pasillos", "Comprobar ofertas antes de salir"]),

        (["limpiar", "limpieza", "ordenar", "casa"],
         ["Recoger lo que este fuera de sitio", "Aspirar la zona principal",
          "Limpiar banos y cocina", "Sacar la basura y el reciclaje"]),

        (["estudiar", "examen", "curso", "aprender", "temario"],
         ["Reunir el material del tema", "Leer el resumen del primer bloque",
          "Hacer un esquema de una pagina", "Resolver dos ejercicios de repaso",
          "Anotar las dudas para la siguiente sesion"]),

        (["viaje", "vacaciones", "vuelo", "hotel"],
         ["Fijar fechas y presupuesto", "Comparar vuelos o trayecto",
          "Reservar alojamiento", "Hacer la lista de equipaje",
          "Comprobar documentacion y seguro"]),

        (["informe", "documento", "memoria", "presentacion", "propuesta"],
         ["Definir en una frase que tiene que quedar claro", "Montar el indice",
          "Escribir el primer apartado", "Reunir los datos y las fuentes",
          "Revisar y recortar lo que sobra"]),

        (["reparar", "averia", "arreglar", "roto", "fuga"],
         ["Localizar exactamente el fallo", "Hacer una foto y buscar el modelo",
          "Reunir las herramientas necesarias", "Probar la reparacion sencilla",
          "Si no sale, pedir presupuesto a un tecnico"]),

        (["fiesta", "cumpleanos", "evento", "cena", "reunion"],
         ["Fijar fecha, hora y sitio", "Hacer la lista de invitados",
          "Enviar las invitaciones", "Preparar la comida o el catering",
          "Comprar decoracion y bebidas"]),

        (["tramite", "papeleo", "hacienda", "renovar", "cita previa", "declaracion", "renta", "impuesto"],
         ["Comprobar que documentos hacen falta", "Reunir y escanear los papeles",
          "Pedir cita previa", "Preparar la carpeta para la cita"]),

        (["gimnasio", "deporte", "correr", "entrenar", "peso"],
         ["Elegir dias y horas fijas de la semana", "Preparar la ropa la noche antes",
          "Hacer la primera sesion corta de 20 minutos", "Anotar como ha ido"]),
    ];

    public string Source => "plantillas";

    public Task<IReadOnlyList<string>> BreakdownAsync(string goal, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(goal);

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var (keywords, steps) in Templates)
        {
            if (keywords.Any(k => Matches(k, normalized, words)))
            {
                return Task.FromResult<IReadOnlyList<string>>(steps.Take(5).ToList());
            }
        }

        return Task.FromResult(Generic(goal.Trim()));
    }

    /// <summary>
    /// La palabra tiene que empezar por la clave, no contenerla en cualquier posicion: si no,
    /// "preparar la declaracion" cae en la plantilla de "reparar" una averia. Las claves de varias
    /// palabras si se buscan tal cual dentro del texto.
    /// </summary>
    private static bool Matches(string keyword, string normalized, string[] words) =>
        keyword.Contains(' ')
            ? normalized.Contains(keyword, StringComparison.Ordinal)
            : words.Any(w => w.StartsWith(keyword, StringComparison.Ordinal));

    /// <summary>
    /// Sin plantilla que encaje, el desglose generico sigue siendo util: obliga a definir el
    /// resultado, a partirlo en un primer trozo pequeno y a fijar cuando se hace.
    /// </summary>
    private static IReadOnlyList<string> Generic(string goal)
    {
        var subject = goal.Length > 60 ? goal[..60].Trim() + "..." : goal;
        return
        [
            $"Escribir en una frase que significa terminar \"{subject}\"",
            "Reunir lo que hace falta para empezar",
            "Hacer el primer trozo pequeno (10 minutos)",
            "Continuar con el siguiente trozo",
            "Revisar el resultado y cerrar la tarea",
        ];
    }

    private static string Normalize(string text)
    {
        var lower = text.ToLowerInvariant();
        var builder = new StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            var mapped = c switch
            {
                'á' => 'a', 'é' => 'e', 'í' => 'i', 'ó' => 'o', 'ú' => 'u', 'ü' => 'u', 'ñ' => 'n',
                _ => c,
            };

            // La puntuacion se convierte en separador para que las palabras salgan limpias.
            builder.Append(char.IsLetterOrDigit(mapped) ? mapped : ' ');
        }

        return builder.ToString();
    }
}
