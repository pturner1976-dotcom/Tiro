namespace Tiro.Cli;

public sealed class TiroInspectService
{
    public TiroInspectSessionsResponse InspectSessions(string databasePath, int limit)
    {
        using var store = TiroStore.Open(databasePath);
        var sessions = store.ListSessionInventory(NormalizeLimit(limit));
        return new TiroInspectSessionsResponse(
            "ok",
            "sessions",
            store.DatabasePath,
            sessions.Count,
            sessions,
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    public TiroInspectOperationalResponse InspectOperational(string databasePath, string? recordType, string? sessionId, int limit)
    {
        using var store = TiroStore.Open(databasePath);
        var normalizedLimit = NormalizeLimit(limit);
        var records = store.ListOperationalRecords(recordType, sessionId, normalizedLimit)
            .Select(record => new TiroOperationalInspectItem(
                record.RecordId,
                record.RecordType,
                record.SessionId,
                record.Origin,
                record.CreatedUtc,
                record.Text))
            .ToArray();
        return new TiroInspectOperationalResponse(
            "ok",
            "operational",
            store.DatabasePath,
            new TiroOperationalInspectFilters(NormalizeOptional(recordType), NormalizeOptional(sessionId), normalizedLimit),
            records.Length,
            records,
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    public TiroInspectSourcesResponse InspectSources(string databasePath, int limit)
    {
        using var store = TiroStore.Open(databasePath);
        var sources = store.ListSourceInventory(NormalizeLimit(limit));
        return new TiroInspectSourcesResponse(
            "ok",
            "sources",
            store.DatabasePath,
            sources.Count,
            sources,
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    public TiroInspectDocumentsResponse InspectDocuments(string databasePath, string? sourceId, int limit)
    {
        using var store = TiroStore.Open(databasePath);
        var normalizedSourceId = NormalizeOptional(sourceId);
        var normalizedLimit = NormalizeLimit(limit);
        var documents = store.ListDocumentInventory(normalizedSourceId, normalizedLimit);
        var warnings = normalizedSourceId is not null && documents.Count == 0
            ? new[] { $"No documents found for source_id '{normalizedSourceId}'. source_id is a database identifier, not a lane name." }
            : Array.Empty<string>();
        return new TiroInspectDocumentsResponse(
            "ok",
            "documents",
            store.DatabasePath,
            new TiroDocumentInspectFilters(normalizedSourceId, normalizedLimit),
            documents.Count,
            documents,
            warnings,
            Array.Empty<string>());
    }

    public TiroInspectRecentResponse InspectRecent(string databasePath, int limit)
    {
        using var store = TiroStore.Open(databasePath);
        var normalizedLimit = NormalizeLimit(limit);
        var operational = store.ListOperationalRecords(null, null, normalizedLimit)
            .Select(record => new TiroOperationalInspectItem(
                record.RecordId,
                record.RecordType,
                record.SessionId,
                record.Origin,
                record.CreatedUtc,
                record.Text))
            .ToArray();
        return new TiroInspectRecentResponse(
            "ok",
            "recent",
            store.DatabasePath,
            store.GetRecentMessagesAcrossSessions(normalizedLimit),
            operational,
            store.ListFacts(null, normalizedLimit),
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    public TiroInspectStatsResponse InspectStats(string databasePath)
    {
        using var store = TiroStore.Open(databasePath);
        return new TiroInspectStatsResponse(
            "ok",
            "stats",
            store.DatabasePath,
            store.GetStats(),
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    public TiroSessionSummaryResponse SessionSummary(string databasePath, string sessionId, int limit)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new TiroSessionSummaryResponse(
                "validation_error",
                Path.GetFullPath(databasePath),
                string.Empty,
                0,
                0,
                null,
                null,
                Array.Empty<Message>(),
                "No session_id supplied.",
                Array.Empty<string>(),
                new[] { "session_id is required." });
        }

        using var store = TiroStore.Open(databasePath);
        var normalizedLimit = NormalizeLimit(limit);
        var session = store.GetSession(sessionId);
        if (session is null)
        {
            return new TiroSessionSummaryResponse(
                "not_found",
                store.DatabasePath,
                sessionId,
                0,
                0,
                null,
                null,
                Array.Empty<Message>(),
                "No stored session exists for the requested session_id.",
                Array.Empty<string>(),
                new[] { $"Session '{sessionId}' was not found." });
        }

        var inventory = store.ListSessionInventory(int.MaxValue).FirstOrDefault(item => item.SessionId == sessionId);
        var total = store.CountSessionMessages(sessionId);
        var messages = store.GetSessionMessagesChronological(sessionId, normalizedLimit);
        var warnings = messages.Count < total
            ? new[] { $"Returned {messages.Count} of {total} messages; summary basis is partial." }
            : Array.Empty<string>();
        return new TiroSessionSummaryResponse(
            "ok",
            store.DatabasePath,
            sessionId,
            total,
            messages.Count,
            inventory?.FirstTimestampUtc,
            inventory?.LastTimestampUtc,
            messages,
            "Chronological session messages returned for session summarization; CLI did not invent a narrative summary.",
            warnings,
            Array.Empty<string>());
    }

    public TiroSessionSearchResponse SessionSearch(string databasePath, string query, int limit, int sessionLimit)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new TiroSessionSearchResponse(
                "validation_error",
                Path.GetFullPath(databasePath),
                string.Empty,
                Array.Empty<string>(),
                NormalizeLimit(sessionLimit),
                0,
                Array.Empty<TiroSessionSearchGroup>(),
                Array.Empty<string>(),
                new[] { "query is required." });
        }

        using var store = TiroStore.Open(databasePath);
        var normalizedLimit = NormalizeLimit(limit);
        var normalizedSessionLimit = NormalizeLimit(sessionLimit <= 0 ? 10 : sessionLimit);
        var sessions = store.ListSessionInventory(normalizedSessionLimit);
        var groups = sessions
            .Select(session => new
            {
                Session = session,
                Matches = store.QuerySessionMessages(session.SessionId, query, normalizedLimit)
            })
            .Where(group => group.Matches.Count > 0)
            .OrderByDescending(group => group.Matches.Max(match => match.Score))
            .ThenByDescending(group => group.Session.LastTimestampUtc)
            .ThenBy(group => group.Session.SessionId, StringComparer.Ordinal)
            .Select(group => new TiroSessionSearchGroup(
                group.Session.SessionId,
                group.Session.MessageCount,
                group.Session.FirstTimestampUtc,
                group.Session.LastTimestampUtc,
                group.Matches.Count,
                group.Matches))
            .ToArray();

        var warnings = store.GetStats().SessionCount > normalizedSessionLimit
            ? new[] { $"Searched the {normalizedSessionLimit} newest sessions, not all stored sessions." }
            : Array.Empty<string>();

        return new TiroSessionSearchResponse(
            "ok",
            store.DatabasePath,
            query.Trim(),
            LexicalSearch.Tokenize(query),
            normalizedSessionLimit,
            groups.Sum(group => group.MatchCount),
            groups,
            warnings,
            Array.Empty<string>());
    }

    public TiroPhraseSearchResponse PhraseSearch(string databasePath, string phrase, string lane, string? sessionId, int limit)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return new TiroPhraseSearchResponse(
                "validation_error",
                Path.GetFullPath(databasePath),
                string.Empty,
                string.IsNullOrWhiteSpace(lane) ? "all" : lane,
                NormalizeOptional(sessionId),
                0,
                Array.Empty<TiroPhraseSearchResult>(),
                Array.Empty<string>(),
                new[] { "phrase is required." });
        }

        using var store = TiroStore.Open(databasePath);
        var normalizedLane = string.IsNullOrWhiteSpace(lane) ? "all" : lane.Trim().ToLowerInvariant();
        try
        {
            var results = store.PhraseSearch(phrase, normalizedLane, NormalizeOptional(sessionId), NormalizeLimit(limit));
            var warnings = normalizedLane == "all" && !string.IsNullOrWhiteSpace(sessionId)
                ? new[] { "session_id filter only constrains session lane when lane=all; use lane=session to test a specific session." }
                : Array.Empty<string>();
            return new TiroPhraseSearchResponse(
                "ok",
                store.DatabasePath,
                phrase,
                normalizedLane,
                NormalizeOptional(sessionId),
                results.Count,
                results,
                warnings,
                Array.Empty<string>());
        }
        catch (ArgumentException ex)
        {
            return new TiroPhraseSearchResponse(
                "validation_error",
                store.DatabasePath,
                phrase,
                normalizedLane,
                NormalizeOptional(sessionId),
                0,
                Array.Empty<TiroPhraseSearchResult>(),
                Array.Empty<string>(),
                new[] { ex.Message });
        }
    }

    private static int NormalizeLimit(int limit) => Math.Clamp(limit <= 0 ? 10 : limit, 1, 500);

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
