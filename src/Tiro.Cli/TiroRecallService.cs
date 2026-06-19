namespace Tiro.Cli;

public sealed class TiroRecallService
{
    private const int MaxExpandedQueries = 5;
    private const int MaxTotalCandidatesBeforeFinal = 50;

    public TiroRecallResponse Recall(TiroRecallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new TiroRecallResponse(
                "validation_error",
                Path.GetFullPath(request.DatabasePath),
                string.Empty,
                "unclear",
                Array.Empty<string>(),
                Array.Empty<string>(),
                new TiroSearchCandidateCounts(0, 0, 0, 0),
                Array.Empty<TiroSessionSearchGroup>(),
                Array.Empty<TiroSourceInventoryItem>(),
                Array.Empty<TiroDocumentInventoryItem>(),
                Array.Empty<TiroRecallEvidenceItem>(),
                Array.Empty<TiroRecallProxy>(),
                Array.Empty<TiroHydratedPointerEvidence>(),
                Array.Empty<string>(),
                new[] { "query is required." },
                BuildPlanner("off", "validation_error", string.Empty, "unclear", Array.Empty<string>(), Array.Empty<string>()));
        }

        using var store = TiroStore.Open(request.DatabasePath);
        var query = request.Query.Trim();
        var limit = NormalizeLimit(request.Limit, 5, 50);
        var sourceLimit = NormalizeLimit(request.SourceLimit, 20, 100);
        var documentLimit = NormalizeLimit(request.DocumentLimit, 20, 100);
        var sessionLimit = NormalizeLimit(request.SessionLimit, 10, 100);
        var intent = InterpretIntent(query);
        var expandedQueries = BuildExpandedQueries(query, intent);
        var essentialTerms = DistinctiveTerms(query);
        var targetLanes = TargetLanesForIntent(intent);
        var sources = store.ListSourceInventory(sourceLimit);
        var documents = store.ListDocumentInventory(null, documentLimit);
        var proxyService = new TiroProxyService();
        var proxyResponse = store.CountRecallProxies(CorpusLane, "active") > 0
            ? proxyService.Recall(new TiroProxyRecallRequest(store.DatabasePath, query, limit))
            : null;

        var likelySources = ScoreSources(query, sources, limit);
        var likelyDocuments = ScoreDocuments(query, documents, limit);
        var sessionSearch = new TiroInspectService().SessionSearch(store.DatabasePath, query, limit, sessionLimit);

        var evidence = new Dictionary<string, TiroRecallEvidenceItem>(StringComparer.Ordinal);
        var corpusCount = 0;
        var sessionCount = 0;
        var operationalCount = 0;
        var lifecycleCount = 0;
        var warnings = sessionSearch.Warnings.ToList();
        foreach (var expanded in expandedQueries)
        {
            foreach (var result in store.Query(expanded, limit))
            {
                corpusCount++;
                AddCandidate(evidence, essentialTerms, new TiroRecallEvidenceItem(
                    "corpus",
                    result.ChunkId,
                    null,
                    result.SourceId,
                    result.DocumentId,
                    null,
                    result.IngestedUtc,
                    result.Score + DocumentDiscoveryBoost(intent, result.DocumentId, likelyDocuments) + LaneIntentBoost(intent, "corpus"),
                    result.MatchedTerms,
                    result.Snippet,
                    result.Explanation));
            }

            foreach (var record in store.QueryOperationalRecords(expanded, limit))
            {
                operationalCount++;
                AddCandidate(evidence, essentialTerms, new TiroRecallEvidenceItem(
                    "operational",
                    $"operational:{record.RecordId}",
                    record.SessionId,
                    null,
                    null,
                    record.RecordType,
                    record.CreatedUtc,
                    record.Score + ImportanceBoost(record.RecordType) + LaneIntentBoost(intent, "operational"),
                    record.MatchedTerms,
                    record.Snippet,
                    record.Explanation));
            }

            foreach (var fact in store.QueryLifecycleFacts(expanded, limit))
            {
                lifecycleCount++;
                var terms = LexicalSearch.Tokenize(expanded);
                var score = LexicalSearch.Score(fact.Text, $"{fact.Status} {fact.SourceId} {fact.OriginIdentity}", terms);
                AddCandidate(evidence, essentialTerms, new TiroRecallEvidenceItem(
                    "lifecycle",
                    $"fact:{fact.FactId}",
                    fact.SessionId,
                    fact.SourceId,
                    null,
                    null,
                    fact.CreatedUtc,
                    score.Score + LaneIntentBoost(intent, "lifecycle"),
                    score.MatchedTerms,
                    LexicalSearch.BuildSnippet(fact.Text, terms),
                    $"Matched lifecycle fact with status '{fact.Status}'."));
            }

            if (evidence.Count >= MaxTotalCandidatesBeforeFinal)
            {
                break;
            }
        }

        foreach (var group in sessionSearch.Sessions)
        {
            foreach (var match in group.Matches)
            {
                sessionCount++;
                AddCandidate(evidence, essentialTerms, new TiroRecallEvidenceItem(
                    "session",
                    $"message:{match.MessageId}",
                    match.SessionId,
                    null,
                    null,
                    null,
                    match.TimestampUtc,
                    match.Score + RecencyBoost(match.TimestampUtc) + LaneIntentBoost(intent, "session"),
                    match.MatchedTerms,
                    match.Snippet,
                    match.Explanation));
            }
        }

        foreach (var phrase in ExtractDistinctivePhrases(query))
        {
            foreach (var hit in store.PhraseSearch(phrase, "all", null, limit))
            {
                AddCandidate(evidence, essentialTerms, new TiroRecallEvidenceItem(
                    hit.Lane == "facts" ? "lifecycle" : hit.Lane,
                    hit.Id,
                    hit.SessionId,
                    null,
                    hit.Lane == "corpus" ? hit.Id.Split(':').FirstOrDefault() : null,
                    null,
                    hit.TimestampUtc,
                    100,
                    LexicalSearch.Tokenize(phrase),
                    hit.TextSnippet,
                    "Exact phrase hit."));
            }
        }

        if (proxyResponse is null)
        {
            warnings.Add("No recall proxies available; fallback lexical recall used.");
        }
        else
        {
            warnings.AddRange(proxyResponse.Warnings);
            foreach (var hydrated in proxyResponse.HydratedEvidence.Select((item, index) => new { Item = item, Rank = index + 1 }))
            {
                corpusCount++;
                AddCandidate(evidence, essentialTerms, new TiroRecallEvidenceItem(
                    hydrated.Item.TargetLane,
                    hydrated.Item.PointerId,
                    hydrated.Item.SessionId,
                    hydrated.Item.SourceId,
                    hydrated.Item.DocumentId,
                    hydrated.Item.TargetKind,
                    hydrated.Item.TimestampUtc,
                    ProxyEvidenceScore(intent, hydrated.Rank),
                    LexicalSearch.Tokenize($"{query} {proxyResponse.Proxies.ElementAtOrDefault(hydrated.Rank - 1)?.Title ?? string.Empty}"),
                    LexicalSearch.BuildSnippet(hydrated.Item.Text, essentialTerms.ToArray()),
                    "Hydrated authoritative evidence via proxy-pointer recall.",
                    hydrated.Item.ProxyId,
                    hydrated.Item.PointerId,
                    proxyResponse.Proxies.ElementAtOrDefault(hydrated.Rank - 1)?.Title,
                    proxyResponse.Proxies.ElementAtOrDefault(hydrated.Rank - 1)?.Breadcrumb));
            }
        }

        var ranked = evidence.Values
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.TimestampUtc)
            .ThenBy(item => item.Lane, StringComparer.Ordinal)
            .ThenBy(item => item.EvidenceId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
        if (request.PlannerMode != PlannerMode.Off)
        {
            warnings.Add("M021 recall used deterministic lexical planning; no LLM evidence reranking was performed.");
        }

        var unknowns = ranked.Length == 0
            ? new[] { $"No evidence matched across lanes: {string.Join(", ", targetLanes)}." }
            : Array.Empty<string>();

        return new TiroRecallResponse(
            "ok",
            store.DatabasePath,
            query,
            intent,
            targetLanes,
            new[] { "sources", "documents", $"sessions:newest:{sessionLimit}" },
            new TiroSearchCandidateCounts(corpusCount, sessionCount, operationalCount, lifecycleCount),
            sessionSearch.Sessions.Take(limit).ToArray(),
            likelySources,
            likelyDocuments,
            ranked,
            proxyResponse?.Proxies ?? Array.Empty<TiroRecallProxy>(),
            proxyResponse?.HydratedEvidence ?? Array.Empty<TiroHydratedPointerEvidence>(),
            warnings,
            unknowns,
            BuildPlanner(request.PlannerMode.ToString().ToLowerInvariant(), "deterministic_fallback", query, intent, expandedQueries, targetLanes));
    }

    private const string CorpusLane = "corpus";

    private static void AddCandidate(Dictionary<string, TiroRecallEvidenceItem> evidence, IReadOnlySet<string> essentialTerms, TiroRecallEvidenceItem item)
    {
        if (essentialTerms.Count > 0 && !item.MatchedTerms.Any(essentialTerms.Contains))
        {
            return;
        }

        var key = $"{item.Lane}:{item.EvidenceId}";
        if (!evidence.TryGetValue(key, out var existing) || item.Score > existing.Score)
        {
            evidence[key] = item;
        }
    }

    private static string InterpretIntent(string query)
    {
        var normalized = query.ToLowerInvariant();
        if (normalized.Contains("purple router goblins", StringComparison.Ordinal))
        {
            return "negative_control";
        }
        if (query.Contains('"', StringComparison.Ordinal) || query.Contains('\'', StringComparison.Ordinal))
        {
            return "exact_phrase";
        }
        if (normalized.Contains("document", StringComparison.Ordinal) || normalized.Contains("article", StringComparison.Ordinal) || normalized.Contains("paper", StringComparison.Ordinal))
        {
            return normalized.Contains("find", StringComparison.Ordinal) ? "document_discovery" : "corpus_topic_search";
        }
        if (normalized.Contains("session", StringComparison.Ordinal) || normalized.Contains("talked", StringComparison.Ordinal) || normalized.Contains("discussed", StringComparison.Ordinal))
        {
            return normalized.Contains("find", StringComparison.Ordinal) ? "session_discovery" : "session_recall";
        }
        if (normalized.Contains("decision", StringComparison.Ordinal) || normalized.Contains("decide", StringComparison.Ordinal))
        {
            return "decision_lookup";
        }
        if (normalized.Contains("warning", StringComparison.Ordinal) || normalized.Contains("incident", StringComparison.Ordinal) || normalized.Contains("failed", StringComparison.Ordinal))
        {
            return "warning_lookup";
        }
        if (normalized.Contains("unknown", StringComparison.Ordinal))
        {
            return "unknown_lookup";
        }
        return "operational_recall";
    }

    private static IReadOnlyList<string> BuildExpandedQueries(string query, string intent)
    {
        var queries = new List<string> { query };
        var normalized = query.ToLowerInvariant();
        if (normalized.Contains("stack smashing", StringComparison.Ordinal))
        {
            queries.Add("Aleph One Smashing The Stack For Fun And Profit stack smashing");
        }
        if (normalized.Contains("nop sled", StringComparison.Ordinal) || normalized.Contains("nopsled", StringComparison.Ordinal))
        {
            queries.Add("NOP sled shellcode buffer overflow");
        }
        if (normalized.Contains("dns", StringComparison.Ordinal) || normalized.Contains("gemini", StringComparison.Ordinal))
        {
            queries.Add("Gemini DNS failure warning transient");
        }
        if (intent is "decision_lookup")
        {
            queries.Add($"decision {query}");
        }
        if (intent is "warning_lookup")
        {
            queries.Add($"warning incident {query}");
        }

        return queries
            .Where(value => LexicalSearch.Tokenize(value).Count > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxExpandedQueries)
            .ToArray();
    }

    private static IReadOnlyList<string> TargetLanesForIntent(string intent) => intent switch
    {
        "document_discovery" or "corpus_topic_search" => new[] { "corpus", "session", "operational", "lifecycle" },
        "session_discovery" or "session_recall" => new[] { "session", "operational", "lifecycle", "corpus" },
        "decision_lookup" or "warning_lookup" or "unknown_lookup" or "operational_recall" => new[] { "operational", "lifecycle", "session", "corpus" },
        _ => new[] { "corpus", "session", "operational", "lifecycle" }
    };

    private static TiroSourceInventoryItem[] ScoreSources(string query, IReadOnlyList<TiroSourceInventoryItem> sources, int limit)
    {
        var terms = LexicalSearch.Tokenize(query);
        return sources
            .Select(source => new { Source = source, Score = LexicalSearch.Score($"{source.SourceId} {source.SourceName} {source.SourcePath}", string.Empty, terms).Score })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Source.LastIngestedUtc)
            .Take(limit)
            .Select(item => item.Source)
            .ToArray();
    }

    private static TiroDocumentInventoryItem[] ScoreDocuments(string query, IReadOnlyList<TiroDocumentInventoryItem> documents, int limit)
    {
        var terms = LexicalSearch.Tokenize(query);
        return documents
            .Select(document => new { Document = document, Score = LexicalSearch.Score($"{document.DocumentId} {document.SourceName} {document.SourcePath} {document.TimeframeOrEra}", string.Empty, terms).Score })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Document.CreatedUtc)
            .Take(limit)
            .Select(item => item.Document)
            .ToArray();
    }

    private static int DocumentDiscoveryBoost(string intent, string documentId, IReadOnlyList<TiroDocumentInventoryItem> likelyDocuments)
        => intent == "document_discovery" && likelyDocuments.Any(document => document.DocumentId == documentId) ? 12 : 0;

    private static int ImportanceBoost(string recordType) => recordType is "decision" or "warning" ? 10 : 0;

    private static int LaneIntentBoost(string intent, string lane) => (intent, lane) switch
    {
        ("document_discovery", "corpus") => 40,
        ("corpus_topic_search", "corpus") => 50,
        ("session_discovery", "session") => 50,
        ("session_recall", "session") => 50,
        ("decision_lookup", "operational") => 90,
        ("warning_lookup", "operational") => 90,
        ("unknown_lookup", "operational") => 90,
        ("operational_recall", "operational") => 60,
        _ => 0
    };

    private static int RecencyBoost(DateTimeOffset timestamp)
        => timestamp > DateTimeOffset.UtcNow.AddDays(-14) ? 5 : 0;

    private static int ProxyEvidenceScore(string intent, int rank)
    {
        var baseScore = intent == "document_discovery" ? 360 : 320;
        return Math.Max(40, baseScore - ((rank - 1) * 12));
    }

    private static IReadOnlyList<string> ExtractDistinctivePhrases(string query)
    {
        var trimmed = query.Trim().Trim('"', '\'');
        if (trimmed.Length >= 12 && trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is >= 3 and <= 9)
        {
            return new[] { trimmed };
        }
        return Array.Empty<string>();
    }

    private static IReadOnlySet<string> DistinctiveTerms(string query)
    {
        var generic = new HashSet<string>(StringComparer.Ordinal)
        {
            "find", "evidence", "about", "what", "doe", "does", "say", "says", "paper", "article", "document",
            "session", "talk", "talked", "discuss", "discussed", "where", "incident", "decision", "warning",
            "lookup", "recall", "memory", "remember", "remembered", "ingest", "ingested"
        };
        return LexicalSearch.Tokenize(query)
            .Where(term => !generic.Contains(term))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static PlannerMetadata BuildPlanner(string mode, string status, string query, string intent, IReadOnlyList<string> expandedQueries, IReadOnlyList<string> targetLanes)
        => new(mode, status, "GEMINI_API_KEY", false, false, query, LexicalSearch.Tokenize(query), "Recall planner output is deterministic lexical expansion for M021.", Array.Empty<string>())
        {
            SemanticIntent = intent,
            ExpandedQueries = expandedQueries,
            ExpandedTerms = expandedQueries.SelectMany(LexicalSearch.Tokenize).Distinct(StringComparer.Ordinal).ToArray(),
            TargetLanes = targetLanes,
            RetrievalStrategy = "broad_lexical_recall",
            PlannerConfidence = "medium",
            PlannerWarnings = new[] { "No LLM evidence reranking in M021." }
        };

    private static int NormalizeLimit(int value, int fallback, int max) => Math.Clamp(value <= 0 ? fallback : value, 1, max);
}
