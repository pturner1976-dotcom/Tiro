using System.Text.Json.Serialization;

namespace Tiro.Cli;

public sealed record TiroQueryRequest(
    [property: JsonPropertyName("database_path")] string DatabasePath,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("limit")] int Limit = 5,
    [property: JsonPropertyName("source_id")] string? SourceId = null,
    [property: JsonPropertyName("document_id")] string? DocumentId = null,
    [property: JsonPropertyName("context_window")] int ContextWindow = 0,
    [property: JsonPropertyName("session_id")] string? SessionId = null,
    [property: JsonPropertyName("planner_mode")] PlannerMode PlannerMode = PlannerMode.Auto,
    [property: JsonPropertyName("debug_planner")] bool DebugPlanner = false,
    [property: JsonPropertyName("include_archived")] bool IncludeArchived = false);

public sealed record TiroQueryResponse(
    [property: JsonPropertyName("packet")] ContextPacket Packet,
    [property: JsonPropertyName("planner_key_name")] string PlannerKeyName,
    [property: JsonPropertyName("planner_key_found")] bool PlannerKeyFound,
    [property: JsonPropertyName("planner_mode")] PlannerMode PlannerMode,
    [property: JsonPropertyName("database_path")] string DatabasePath,
    [property: JsonPropertyName("query")] string Query);

public sealed class TiroQueryService
{
    public async Task<TiroQueryResponse> QueryAsync(
        TiroQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var databasePath = RequireValue(request.DatabasePath, nameof(request.DatabasePath));
        var query = RequireValue(request.Query, nameof(request.Query));
        if (request.Limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Limit), "Limit must be greater than zero.");
        }
        if (request.ContextWindow is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ContextWindow), "Context window must be 0, 1, or 2.");
        }

        var sourceId = NormalizeOptional(request.SourceId);
        var documentId = NormalizeOptional(request.DocumentId);
        var sessionId = NormalizeOptional(request.SessionId);

        using var store = TiroStore.Open(databasePath);
        var plannerConfig = PlannerConfig.Load(request.PlannerMode);
        using var httpClient = new HttpClient { Timeout = plannerConfig.Timeout };
        var planner = new RetrievalPlanner(plannerConfig, new GeminiPlannerClient(httpClient, plannerConfig));
        var plannerResult = await planner.PlanAsync(query, request.DebugPlanner, cancellationToken);
        var filters = new RetrievalFilters(sourceId, documentId);
        var packet = new ContextPacketBuilder(store).Build(
            query,
            request.Limit,
            plannerResult,
            plannerConfig.KeyName,
            filters,
            request.ContextWindow,
            sessionId,
            request.IncludeArchived);
        packet = AddReadModeWarnings(packet, query, sessionId, sourceId, request.IncludeArchived);

        return new TiroQueryResponse(
            packet,
            plannerConfig.KeyName,
            plannerConfig.KeyFound,
            plannerConfig.Mode,
            store.DatabasePath,
            query);
    }

    private static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ContextPacket AddReadModeWarnings(ContextPacket packet, string query, string? sessionId, string? sourceId, bool includeArchived)
    {
        var warnings = packet.Warnings.ToList();
        var normalized = query.Trim().ToLowerInvariant();
        if (sourceId is not null && IsLaneName(sourceId))
        {
            warnings.Add(new WarningRecord("source_id appears to be a lane name, not a database source_id. Use tiro_recall or inspect sources."));
        }

        if ((normalized.Contains("list", StringComparison.Ordinal) || normalized.Contains("show", StringComparison.Ordinal))
            && (normalized.Contains("session", StringComparison.Ordinal) || normalized.Contains("sessions", StringComparison.Ordinal)))
        {
            warnings.Add(new WarningRecord("This appears to be an inventory request. Use tiro_inspect sessions."));
        }

        if (normalized.Contains("summarize", StringComparison.Ordinal)
            && normalized.Contains("session", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(sessionId))
        {
            warnings.Add(new WarningRecord("This appears to be a whole-session recall request. Use tiro_session_summary."));
        }

        if (includeArchived)
        {
            warnings.Add(new WarningRecord("Archived operational and lifecycle evidence was explicitly included for this query."));
        }

        return warnings.Count == packet.Warnings.Count ? packet : packet with { Warnings = warnings };
    }

    private static bool IsLaneName(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "corpus" or "session" or "operational" or "lifecycle";
    }
}
