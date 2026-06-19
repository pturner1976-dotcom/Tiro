using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Tiro.Cli;

public interface IRetrievalPlannerClient
{
    Task<PlannerClientResult> PlanAsync(string query, IReadOnlyList<string> deterministicTerms, CancellationToken cancellationToken);
}

public sealed class GeminiPlannerClient : IRetrievalPlannerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly HttpClient _httpClient;
    private readonly PlannerConfig _config;

    public GeminiPlannerClient(HttpClient httpClient, PlannerConfig config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<PlannerClientResult> PlanAsync(string query, IReadOnlyList<string> deterministicTerms, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            throw new InvalidOperationException("Planner API key is not configured.");
        }

        var endpoint = BuildEndpoint(_config.Model, _config.ApiKey);
        var diagnostics = PlannerHttpDiagnostics.ForRequest(endpoint.RedactedUrl, _config.Model, "query_parameter", _config.Timeout);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            endpoint.Url);
        request.Content = JsonContent.Create(BuildRequest(query, deterministicTerms), options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new PlannerClientException(
                "Planner HTTP request failed before a response was received.",
                diagnostics with { InnerExceptionMessage = ex.InnerException?.Message ?? ex.Message },
                ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            diagnostics = diagnostics with
            {
                HttpStatusCode = (int)response.StatusCode,
                ResponseBody = LimitDiagnosticBody(body)
            };

            if (!response.IsSuccessStatusCode)
            {
                throw new PlannerClientException(
                    "Planner HTTP response was not successful.",
                    diagnostics,
                    null);
            }

            try
            {
                var geminiResponse = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(body, JsonOptions)
                    ?? throw new InvalidOperationException("Planner response was empty.");
                var text = geminiResponse.Candidates?
                    .SelectMany(candidate => candidate.Content?.Parts ?? Array.Empty<GeminiPart>())
                    .Select(part => part.Text)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new InvalidOperationException("Planner response did not include text.");
                }

                return new PlannerClientResult(PlannerAdvice.Parse(text), diagnostics);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new PlannerClientException(
                    "Planner response could not be parsed.",
                    diagnostics with { InnerExceptionMessage = ex.Message },
                    ex);
            }
        }
    }

    private static GeminiEndpoint BuildEndpoint(string model, string apiKey)
    {
        var baseUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
        return new GeminiEndpoint(
            $"{baseUrl}?key={Uri.EscapeDataString(apiKey)}",
            $"{baseUrl}?key=<redacted>");
    }

    private static string? LimitDiagnosticBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var compact = Regex.Replace(body, @"\s+", " ").Trim();
        return compact.Length > 1200 ? compact[..1200] : compact;
    }

    private static GeminiGenerateContentRequest BuildRequest(string query, IReadOnlyList<string> deterministicTerms)
    {
        var prompt =
            "Return exactly one compact JSON object for Tiro retrieval planning. " +
            "No markdown, no code fences, no prose, no preamble. " +
            "Do not answer the user, claim evidence exists, invent facts, invent session IDs, or write memory. " +
            "Produce bounded lexical search advice that deterministic Tiro code will execute and audit. " +
            "Required keys: search_query, refined_terms, packet_focus, semantic_intent, expanded_queries, expanded_terms, target_lanes, required_entities, optional_entities, likely_session_scope, retrieval_strategy, confidence, planner_warnings. " +
            "Allowed semantic_intent: exact_lookup, topic_search, session_recall, session_summary, operational_recall, decision_lookup, warning_lookup, unknown_lookup, current_state, archive_lookup, negative_control, unclear. " +
            "Allowed target_lanes: corpus, session, operational, lifecycle. Allowed retrieval_strategy: single_query, multi_query_union, session_direct_summary, lane_focused, exact_term. " +
            "Use short lexical-search-friendly expanded_queries, max 5. Use expanded_terms max 25. Preserve named entities from the query. Mark broad saved-session summary requests as session_summary or session_recall.\n" +
            $"User query: {query}\n" +
            $"Deterministic terms: {string.Join(", ", deterministicTerms)}";

        return new GeminiGenerateContentRequest(
            new[]
            {
                new GeminiContent(new[] { new GeminiPart(prompt) })
            },
            new GeminiGenerationConfig(0.0, 768, "application/json", PlannerResponseSchema.Create()));
    }
}

public sealed record PlannerClientResult(PlannerAdvice Advice, PlannerHttpDiagnostics Diagnostics);

public sealed record PlannerHttpDiagnostics(
    string EndpointUrl,
    string Model,
    string AuthMethod,
    int TimeoutMilliseconds,
    int? HttpStatusCode,
    string? ResponseBody,
    string? InnerExceptionMessage)
{
    public static PlannerHttpDiagnostics ForRequest(string endpointUrl, string model, string authMethod, TimeSpan timeout) => new(
        endpointUrl,
        model,
        authMethod,
        (int)timeout.TotalMilliseconds,
        null,
        null,
        null);

    public IReadOnlyList<string> ToDebugLines() => new[]
    {
        $"gemini_endpoint_url={EndpointUrl}",
        $"gemini_model={Model}",
        $"gemini_auth_method={AuthMethod}",
        $"gemini_timeout_ms={TimeoutMilliseconds}",
        $"gemini_http_status_code={HttpStatusCode?.ToString() ?? "none"}",
        $"gemini_response_body={ResponseBody ?? "none"}",
        $"gemini_inner_exception={InnerExceptionMessage ?? "none"}"
    };
}

public sealed class PlannerClientException : Exception
{
    public PlannerClientException(string message, PlannerHttpDiagnostics diagnostics, Exception? innerException)
        : base(message, innerException)
    {
        Diagnostics = diagnostics;
    }

    public PlannerHttpDiagnostics Diagnostics { get; }
}

internal sealed record GeminiEndpoint(string Url, string RedactedUrl);

public sealed record PlannerAdvice(
    string SearchQuery,
    IReadOnlyList<string> RefinedTerms,
    IReadOnlyList<string> PacketFocus,
    IReadOnlyList<string> Warnings,
    string SemanticIntent,
    IReadOnlyList<string> ExpandedQueries,
    IReadOnlyList<string> ExpandedTerms,
    IReadOnlyList<string> TargetLanes,
    IReadOnlyList<string> RequiredEntities,
    IReadOnlyList<string> OptionalEntities,
    string? LikelySessionScope,
    string RetrievalStrategy,
    string Confidence,
    IReadOnlyList<string> PlannerWarnings)
{
    public PlannerAdvice(string searchQuery, IReadOnlyList<string> refinedTerms, IReadOnlyList<string> packetFocus)
        : this(searchQuery, refinedTerms, packetFocus, Array.Empty<string>())
    {
    }

    public PlannerAdvice(string searchQuery, IReadOnlyList<string> refinedTerms, IReadOnlyList<string> packetFocus, IReadOnlyList<string> warnings)
        : this(
            searchQuery,
            refinedTerms,
            packetFocus,
            warnings,
            "unclear",
            new[] { searchQuery },
            refinedTerms,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            null,
            "single_query",
            "low",
            warnings)
    {
    }

    public static PlannerAdvice Empty(string query) => new(query, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    public static PlannerAdvice Parse(string text)
    {
        var json = ExtractJsonObject(text);
        var raw = JsonSerializer.Deserialize<RawPlannerAdvice>(json, JsonOptions)
            ?? throw new InvalidOperationException("Planner JSON was empty.");
        var searchQuery = SanitizeSearchQuery(raw.SearchQuery);
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            throw new InvalidOperationException("Planner did not return a usable search_query.");
        }
        if (raw.RefinedTerms is null)
        {
            throw new InvalidOperationException("Planner JSON is missing required refined_terms.");
        }
        if (raw.PacketFocus is null)
        {
            throw new InvalidOperationException("Planner JSON is missing required packet_focus.");
        }

        var semanticFieldsPresent = raw.SemanticIntent is not null
            || raw.ExpandedQueries is not null
            || raw.ExpandedTerms is not null
            || raw.TargetLanes is not null
            || raw.RetrievalStrategy is not null
            || raw.Confidence is not null
            || raw.PlannerWarnings is not null;

        if (semanticFieldsPresent)
        {
            RequireSemantic(raw.SemanticIntent, "semantic_intent");
            RequireSemantic(raw.ExpandedQueries, "expanded_queries");
            RequireSemantic(raw.ExpandedTerms, "expanded_terms");
            RequireSemantic(raw.TargetLanes, "target_lanes");
            RequireSemantic(raw.RequiredEntities, "required_entities");
            RequireSemantic(raw.OptionalEntities, "optional_entities");
            RequireSemantic(raw.RetrievalStrategy, "retrieval_strategy");
            RequireSemantic(raw.Confidence, "confidence");
            RequireSemantic(raw.PlannerWarnings, "planner_warnings");
        }

        var refinedTerms = SanitizeTerms(raw.RefinedTerms);
        var warnings = SanitizeWarnings(raw.Warnings);
        var expandedTerms = SanitizeTerms(raw.ExpandedTerms, 25);
        return new PlannerAdvice(
            searchQuery,
            refinedTerms,
            SanitizeTerms(raw.PacketFocus),
            warnings,
            SanitizeChoice(raw.SemanticIntent, AllowedSemanticIntents, "unclear"),
            SanitizeQueries(raw.ExpandedQueries, searchQuery),
            expandedTerms.Count == 0 ? refinedTerms : expandedTerms,
            SanitizeChoiceList(raw.TargetLanes, AllowedLanes, 4),
            SanitizeEntityList(raw.RequiredEntities),
            SanitizeEntityList(raw.OptionalEntities),
            SanitizeOptional(raw.LikelySessionScope),
            SanitizeChoice(raw.RetrievalStrategy, AllowedStrategies, "single_query"),
            SanitizeChoice(raw.Confidence, AllowedConfidence, "low"),
            SanitizeWarnings(raw.PlannerWarnings).Concat(warnings).Distinct(StringComparer.Ordinal).Take(6).ToArray());
    }

    private static void RequireSemantic<T>(T? value, string name)
    {
        if (value is null)
        {
            throw new InvalidOperationException($"Planner JSON is missing required {name}.");
        }
    }

    private static string ExtractJsonObject(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        var fenced = TryUnwrapSingleCodeFence(trimmed);
        if (fenced is not null)
        {
            return ExtractJsonObject(fenced);
        }

        var withoutPreamble = TryRemoveSinglePreambleLine(trimmed);
        if (withoutPreamble is not null)
        {
            return ExtractJsonObject(withoutPreamble);
        }

        throw new InvalidOperationException("Planner response was not a single JSON object.");
    }

    private static string? TryUnwrapSingleCodeFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return null;
        }

        var firstLineEnd = text.IndexOf('\n');
        if (firstLineEnd < 0)
        {
            return null;
        }

        var fenceHeader = text[..firstLineEnd].Trim();
        if (fenceHeader is not "```" and not "```json" and not "```JSON")
        {
            return null;
        }

        var body = text[(firstLineEnd + 1)..];
        if (!body.EndsWith("```", StringComparison.Ordinal))
        {
            return null;
        }

        body = body[..^3].Trim();
        return body.Contains("```", StringComparison.Ordinal) ? null : body;
    }

    private static string? TryRemoveSinglePreambleLine(string text)
    {
        var firstLineEnd = text.IndexOf('\n');
        if (firstLineEnd < 0)
        {
            return null;
        }

        var firstLine = text[..firstLineEnd].Trim();
        var rest = text[(firstLineEnd + 1)..].Trim();
        if (firstLine.Length == 0 || firstLine.Contains('{', StringComparison.Ordinal) || firstLine.Contains('}', StringComparison.Ordinal))
        {
            return null;
        }

        return rest.StartsWith('{') && rest.EndsWith('}') ? rest : null;
    }

    private static string SanitizeSearchQuery(string? value)
    {
        var query = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        return query.Length > 240 ? query[..240] : query;
    }

    private static IReadOnlyList<string> SanitizeTerms(IReadOnlyList<string>? values, int maxItems = 8)
    {
        if (values is null)
        {
            return Array.Empty<string>();
        }

        return values
            .SelectMany(value => LexicalSearch.Tokenize(value))
            .Distinct(StringComparer.Ordinal)
            .Take(maxItems)
            .ToArray();
    }

    private static IReadOnlyList<string> SanitizeQueries(IReadOnlyList<string>? values, string fallback)
    {
        var queries = (values ?? Array.Empty<string>())
            .Select(value => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim())
            .Where(value => value.Length > 0)
            .Select(value => value.Length > 160 ? value[..160] : value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        if (queries.Count == 0)
        {
            queries.Add(fallback);
        }

        return queries;
    }

    private static string SanitizeChoice(string? value, IReadOnlySet<string> allowed, string fallback)
    {
        var normalized = Regex.Replace(value ?? string.Empty, @"\s+", "_").Trim().ToLowerInvariant();
        return allowed.Contains(normalized) ? normalized : fallback;
    }

    private static IReadOnlyList<string> SanitizeChoiceList(IReadOnlyList<string>? values, IReadOnlySet<string> allowed, int maxItems)
    {
        return (values ?? Array.Empty<string>())
            .Select(value => SanitizeChoice(value, allowed, string.Empty))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(maxItems)
            .ToArray();
    }

    private static IReadOnlyList<string> SanitizeEntityList(IReadOnlyList<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Select(value => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim())
            .Where(value => value.Length > 0)
            .Select(value => value.Length > 80 ? value[..80] : value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static string? SanitizeOptional(string? value)
    {
        var normalized = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        return normalized.Length == 0 ? null : normalized.Length > 80 ? normalized[..80] : normalized;
    }

    private static IReadOnlyList<string> SanitizeWarnings(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return Array.Empty<string>();
        }

        return values
            .Select(value => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim())
            .Where(value => value.Length > 0)
            .Select(value => value.Length > 160 ? value[..160] : value)
            .Take(4)
            .ToArray();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedSemanticIntents = new(StringComparer.Ordinal)
    {
        "exact_lookup", "topic_search", "session_recall", "session_summary", "operational_recall",
        "decision_lookup", "warning_lookup", "unknown_lookup", "current_state", "archive_lookup",
        "negative_control", "unclear"
    };
    private static readonly HashSet<string> AllowedLanes = new(StringComparer.Ordinal)
    {
        "corpus", "session", "operational", "lifecycle"
    };
    private static readonly HashSet<string> AllowedStrategies = new(StringComparer.Ordinal)
    {
        "single_query", "multi_query_union", "session_direct_summary", "lane_focused", "exact_term"
    };
    private static readonly HashSet<string> AllowedConfidence = new(StringComparer.Ordinal)
    {
        "low", "medium", "high"
    };
}

public sealed record PlannerRunResult(
    PlannerMode Mode,
    string Status,
    bool KeyFound,
    bool EnvFileFound,
    string? Model,
    string RetrievalQuery,
    IReadOnlyList<string> RefinedTerms,
    string Message,
    IReadOnlyList<string> Debug,
    PlannerAdvice Advice)
{
    public static PlannerRunResult Disabled(PlannerConfig config, string query) => new(
        config.Mode,
        "disabled",
        config.KeyFound,
        config.EnvFileFound,
        null,
        query,
        Array.Empty<string>(),
        "Planner disabled; deterministic retrieval used.",
        Array.Empty<string>(),
        PlannerAdvice.Empty(query));

    public static PlannerRunResult Unavailable(PlannerConfig config, string query, string reason) => new(
        config.Mode,
        "unavailable",
        config.KeyFound,
        config.EnvFileFound,
        null,
        query,
        Array.Empty<string>(),
        reason,
        Array.Empty<string>(),
        PlannerAdvice.Empty(query));

    public static PlannerRunResult Failed(
        PlannerConfig config,
        string query,
        string reason,
        IReadOnlyList<string>? debug = null) => new(
        config.Mode,
        "failed",
        config.KeyFound,
        config.EnvFileFound,
        config.Model,
        query,
        Array.Empty<string>(),
        reason,
        debug ?? Array.Empty<string>(),
        PlannerAdvice.Empty(query));

    public static PlannerRunResult Used(
        PlannerConfig config,
        string originalQuery,
        PlannerAdvice advice,
        PlannerHttpDiagnostics diagnostics,
        bool includeDebug) => new(
        config.Mode,
        "used",
        config.KeyFound,
        config.EnvFileFound,
        config.Model,
        advice.SearchQuery,
        advice.RefinedTerms,
        "Planner guidance used for retrieval query refinement.",
        includeDebug
            ? new[]
            {
                $"original_query_length={originalQuery.Length}",
                $"retrieval_query_length={advice.SearchQuery.Length}",
                $"refined_term_count={advice.RefinedTerms.Count}",
                $"packet_focus_count={advice.PacketFocus.Count}",
                $"semantic_intent={advice.SemanticIntent}",
                $"expanded_query_count={advice.ExpandedQueries.Count}",
                $"retrieval_strategy={advice.RetrievalStrategy}"
            }
                .Concat(diagnostics.ToDebugLines())
                .ToArray()
            : Array.Empty<string>(),
        advice);
}

public sealed class RetrievalPlanner
{
    private readonly PlannerConfig _config;
    private readonly IRetrievalPlannerClient _client;

    public RetrievalPlanner(PlannerConfig config, IRetrievalPlannerClient client)
    {
        _config = config;
        _client = client;
    }

    public async Task<PlannerRunResult> PlanAsync(string query, bool debugPlanner, CancellationToken cancellationToken)
    {
        if (_config.Mode == PlannerMode.Off)
        {
            return PlannerRunResult.Disabled(_config, query);
        }

        if (!_config.KeyFound)
        {
            var reason = _config.EnvFileFound
                ? $"Planner key {_config.KeyName} was not found; deterministic retrieval used."
                : $"Planner env file was not found and {_config.KeyName} was not set; deterministic retrieval used.";
            return PlannerRunResult.Unavailable(_config, query, reason);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_config.Timeout);
        try
        {
            var result = await _client.PlanAsync(query, LexicalSearch.Tokenize(query), timeout.Token).ConfigureAwait(false);
            if (LexicalSearch.Tokenize(result.Advice.SearchQuery).Count == 0)
            {
                return PlannerRunResult.Failed(_config, query, "Planner returned no searchable terms; deterministic retrieval used.");
            }

            return PlannerRunResult.Used(_config, query, result.Advice, result.Diagnostics, debugPlanner);
        }
        catch (PlannerClientException ex)
        {
            return PlannerRunResult.Failed(
                _config,
                query,
                $"Planner failed: {ex.GetType().Name}; deterministic retrieval used.",
                debugPlanner ? ex.Diagnostics.ToDebugLines() : Array.Empty<string>());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            var debug = debugPlanner
                ? PlannerHttpDiagnostics
                    .ForRequest(
                        $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(_config.Model)}:generateContent?key=<redacted>",
                        _config.Model,
                        "query_parameter",
                        _config.Timeout)
                    .ToDebugLines()
                    .Concat(new[] { $"planner_exception_message={ex.Message}" })
                    .ToArray()
                : Array.Empty<string>();
            return PlannerRunResult.Failed(_config, query, $"Planner failed: {ex.GetType().Name}; deterministic retrieval used.", debug);
        }
    }
}

internal sealed record GeminiGenerateContentRequest(
    [property: JsonPropertyName("contents")] IReadOnlyList<GeminiContent> Contents,
    [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig GenerationConfig);

internal sealed record GeminiContent(
    [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);

internal sealed record GeminiPart(
    [property: JsonPropertyName("text")] string Text);

internal sealed record GeminiGenerationConfig(
    [property: JsonPropertyName("temperature")] double Temperature,
    [property: JsonPropertyName("maxOutputTokens")] int MaxOutputTokens,
    [property: JsonPropertyName("responseMimeType")] string ResponseMimeType,
    [property: JsonPropertyName("responseJsonSchema")] PlannerResponseSchema ResponseJsonSchema);

internal sealed record PlannerResponseSchema(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("properties")] IReadOnlyDictionary<string, PlannerResponseSchema>? Properties,
    [property: JsonPropertyName("items")] PlannerResponseSchema? Items,
    [property: JsonPropertyName("required")] IReadOnlyList<string>? Required,
    [property: JsonPropertyName("propertyOrdering")] IReadOnlyList<string>? PropertyOrdering)
{
    public static PlannerResponseSchema Create()
    {
        var stringSchema = new PlannerResponseSchema("string", null, null, null, null);
        var stringArray = new PlannerResponseSchema("array", null, stringSchema, null, null);
        return new PlannerResponseSchema(
            "object",
            new Dictionary<string, PlannerResponseSchema>
            {
                ["search_query"] = stringSchema,
                ["refined_terms"] = stringArray,
                ["packet_focus"] = stringArray,
                ["warnings"] = stringArray,
                ["semantic_intent"] = stringSchema,
                ["expanded_queries"] = stringArray,
                ["expanded_terms"] = stringArray,
                ["target_lanes"] = stringArray,
                ["required_entities"] = stringArray,
                ["optional_entities"] = stringArray,
                ["likely_session_scope"] = stringSchema,
                ["retrieval_strategy"] = stringSchema,
                ["confidence"] = stringSchema,
                ["planner_warnings"] = stringArray
            },
            null,
            new[] { "search_query", "refined_terms", "packet_focus", "semantic_intent", "expanded_queries", "expanded_terms", "target_lanes", "required_entities", "optional_entities", "retrieval_strategy", "confidence", "planner_warnings" },
            new[] { "search_query", "refined_terms", "packet_focus", "semantic_intent", "expanded_queries", "expanded_terms", "target_lanes", "required_entities", "optional_entities", "likely_session_scope", "retrieval_strategy", "confidence", "planner_warnings", "warnings" });
    }
}

internal sealed record GeminiGenerateContentResponse(
    [property: JsonPropertyName("candidates")] IReadOnlyList<GeminiCandidate>? Candidates);

internal sealed record GeminiCandidate(
    [property: JsonPropertyName("content")] GeminiContent? Content);

internal sealed record RawPlannerAdvice(
    [property: JsonPropertyName("search_query")] string? SearchQuery,
    [property: JsonPropertyName("refined_terms")] IReadOnlyList<string>? RefinedTerms,
    [property: JsonPropertyName("packet_focus")] IReadOnlyList<string>? PacketFocus,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string>? Warnings,
    [property: JsonPropertyName("semantic_intent")] string? SemanticIntent,
    [property: JsonPropertyName("expanded_queries")] IReadOnlyList<string>? ExpandedQueries,
    [property: JsonPropertyName("expanded_terms")] IReadOnlyList<string>? ExpandedTerms,
    [property: JsonPropertyName("target_lanes")] IReadOnlyList<string>? TargetLanes,
    [property: JsonPropertyName("required_entities")] IReadOnlyList<string>? RequiredEntities,
    [property: JsonPropertyName("optional_entities")] IReadOnlyList<string>? OptionalEntities,
    [property: JsonPropertyName("likely_session_scope")] string? LikelySessionScope,
    [property: JsonPropertyName("retrieval_strategy")] string? RetrievalStrategy,
    [property: JsonPropertyName("confidence")] string? Confidence,
    [property: JsonPropertyName("planner_warnings")] IReadOnlyList<string>? PlannerWarnings);
