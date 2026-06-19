namespace Tiro.Cli;

public sealed class ContextPacketBuilder
{
    private readonly TiroStore _store;

    public ContextPacketBuilder(TiroStore store)
    {
        _store = store;
    }

    public ContextPacket Build(string query, int limit, PlannerRunResult planner, string keyName, RetrievalFilters filters, int contextWindow, string? sessionId = null)
    {
        var retrievalQuery = string.IsNullOrWhiteSpace(planner.RetrievalQuery) ? query : planner.RetrievalQuery;
        var expandedQueries = BuildExpandedQueries(query, retrievalQuery, planner.Advice);
        var terms = BuildTerms(retrievalQuery, planner.Advice);
        contextWindow = Math.Clamp(contextWindow, 0, 2);
        var queryDiagnostics = new List<ExpandedQueryDiagnostic>();
        var results = QueryCorpus(expandedQueries, limit, filters, queryDiagnostics);
        var supportingContext = _store.GetSupportingContext(results, contextWindow);
        var sessionEvidence = string.IsNullOrWhiteSpace(sessionId)
            ? Array.Empty<SessionMessageEvidence>()
            : QuerySession(expandedQueries, Math.Min(limit, 5), sessionId, queryDiagnostics).ToArray();
        var recentSessionContext = string.IsNullOrWhiteSpace(sessionId)
            ? Array.Empty<Message>()
            : _store.GetRecentMessages(sessionId, 4).ToArray();
        var operationalMemory = QueryOperational(expandedQueries, Math.Min(limit, 5), sessionId, queryDiagnostics).ToArray();
        var lifecycleFacts = QueryLifecycle(expandedQueries, limit, sessionId, queryDiagnostics);

        var sourceIds = results
            .Select(result => result.SourceId)
            .Concat(supportingContext.Select(context => context.SourceId))
            .Concat(operationalMemory.SelectMany(record => record.LinkedSourceIds))
            .Concat(lifecycleFacts.Select(fact => fact.SourceId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var sources = _store.GetSourcesByIds(sourceIds);

        var factCandidates = results
            .Select(result => new FactRecord(
                0,
                result.IngestedUtc,
                $"{result.Snippet} [source={result.SourceId}; chunk={result.ChunkId}]",
                "active",
                result.SourceId,
                "",
                null,
                new[] { result.ChunkId },
                Array.Empty<long>(),
                Array.Empty<long>(),
                null,
                null,
                null,
                null)
                { EvidenceType = "corpus" })
            .Concat(sessionEvidence.Select(result => new FactRecord(
                0,
                result.TimestampUtc,
                $"{result.Snippet} [session={result.SessionId}; message={result.MessageId}; source_identity={result.SourceIdentity}]",
                "active",
                $"session:{result.SessionId}",
                "",
                result.SessionId,
                Array.Empty<string>(),
                new[] { result.MessageId },
                Array.Empty<long>(),
                null,
                null,
                null,
                null)
                { EvidenceType = "session" }))
            .Concat(operationalMemory
                .Where(result => result.RecordType == "decision")
                .Select(result => new FactRecord(
                    0,
                    result.CreatedUtc,
                    $"{result.Text} [operational={result.RecordType}; record={result.RecordId}; origin={result.Origin}]",
                    "active",
                    $"operational:{result.RecordType}",
                    "",
                    null,
                    Array.Empty<string>(),
                    Array.Empty<long>(),
                    new[] { result.RecordId },
                    null,
                    null,
                    null,
                    null)
                    { EvidenceType = "operational" }))
            .Concat(lifecycleFacts.OrderBy(fact => fact.Status == TiroStore.FactStatus.Active ? 0 : 1).ThenByDescending(fact => fact.FactId))
            .ToArray();
        var retrievalPolicy = RetrievalPolicy.Build(terms, results, sessionEvidence, operationalMemory, lifecycleFacts);
        var facts = factCandidates
            .OrderByDescending(fact => RetrievalPolicy.FindSignal(retrievalPolicy, fact)?.FinalScore ?? 0)
            .ThenBy(fact => RetrievalPolicy.FactKey(fact), StringComparer.Ordinal)
            .ToArray();

        var unknowns = new List<Unknown>();
        var warnings = new List<WarningRecord>
        {
            new("Deterministic lexical retrieval only; no semantic embeddings, web retrieval, or reranking were used.")
        };

        if (terms.Count == 0)
        {
            unknowns.Add(new Unknown("No searchable query terms remained after normalization."));
        }
        else if (results.Count == 0 && filters.HasAny && !_store.HasChunksForFilters(filters))
        {
            unknowns.Add(new Unknown("No stored chunks matched the requested source/document filters."));
        }
        else if (results.Count == 0 && filters.HasAny)
        {
            unknowns.Add(new Unknown("Stored chunks matched the requested filters, but none matched the normalized query terms."));
        }
        else if (results.Count == 0)
        {
            unknowns.Add(new Unknown("No stored chunks matched the normalized query terms."));
        }

        // M008: Fact lifecycle warnings
        if (lifecycleFacts.Count == 0)
        {
            warnings.Add(new WarningRecord("No matching lifecycle-aware fact evidence was found for this query."));
        }
        else
        {
            var activeCount = lifecycleFacts.Count(f => f.Status == TiroStore.FactStatus.Active);
            if (activeCount == 0)
            {
                var staleCount = lifecycleFacts.Count(f => f.Status == TiroStore.FactStatus.Stale);
                var supersededCount = lifecycleFacts.Count(f => f.Status == TiroStore.FactStatus.Superseded);
                var conflictingCount = lifecycleFacts.Count(f => f.Status == TiroStore.FactStatus.Conflicting);
                if (staleCount > 0 || supersededCount > 0 || conflictingCount > 0)
                {
                    warnings.Add(new WarningRecord($"Lifecycle facts matching this query are only stale/superseded/conflicting: stale={staleCount}, superseded={supersededCount}, conflicting={conflictingCount}."));
                }
            }
        }
        var topPolicySignal = retrievalPolicy.Signals.FirstOrDefault();
        if (topPolicySignal is not null)
        {
            warnings.Add(new WarningRecord($"Retrieval policy mode={retrievalPolicy.QueryMode}; top evidence={topPolicySignal.EvidenceType}/{topPolicySignal.EvidenceLabel}; {topPolicySignal.Explanation}."));
            if (topPolicySignal.LifecycleScore < 0)
            {
                warnings.Add(new WarningRecord($"Best weighted evidence has lifecycle limitation: state={topPolicySignal.LifecycleState}, freshness={topPolicySignal.FreshnessHint}."));
            }
        }
        var combinedMatchedTerms = results
            .SelectMany(result => result.MatchedTerms)
            .Concat(sessionEvidence.SelectMany(result => result.MatchedTerms))
            .Concat(operationalMemory.SelectMany(result => result.MatchedTerms))
            .Concat(lifecycleFacts.SelectMany(fact => LexicalSearch.Score(fact.Text, $"{fact.Status} {fact.SourceId} {fact.OriginIdentity}", terms).MatchedTerms))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var missingTerms = terms.Except(combinedMatchedTerms, StringComparer.Ordinal).ToArray();
        if ((results.Count > 0 || sessionEvidence.Length > 0) && missingTerms.Length > 0)
        {
            unknowns.Add(new Unknown($"Combined corpus/session/operational evidence did not match every normalized query term: {string.Join(", ", missingTerms)}."));
        }
        if (!string.IsNullOrWhiteSpace(sessionId) && recentSessionContext.Length == 0)
        {
            unknowns.Add(new Unknown($"No recent session/state messages are stored for session '{sessionId}'."));
        }
        else if (!string.IsNullOrWhiteSpace(sessionId) && sessionEvidence.Length == 0)
        {
            unknowns.Add(new Unknown($"Recent session/state messages exist for session '{sessionId}', but none matched the normalized query terms."));
        }
        foreach (var operationalUnknown in operationalMemory.Where(record => record.RecordType == "unknown"))
        {
            unknowns.Add(new Unknown($"Operational unknown #{operationalUnknown.RecordId}: {operationalUnknown.Text}"));
        }
        warnings.Add(new WarningRecord(planner.Message));
        foreach (var plannerWarning in planner.Advice.PlannerWarnings)
        {
            warnings.Add(new WarningRecord($"Planner warning: {plannerWarning}"));
        }
        if (planner.Advice.SemanticIntent is "session_summary" or "session_recall" && string.IsNullOrWhiteSpace(sessionId))
        {
            warnings.Add(new WarningRecord("Planner detected broad session recall/summary intent, but no session_id was supplied; retrieval may be fragmentary."));
        }
        if (expandedQueries.Count > 1)
        {
            warnings.Add(new WarningRecord($"Planner expanded retrieval into {expandedQueries.Count} lexical queries; explicit filters remained hard constraints."));
        }
        if (filters.HasAny)
        {
            warnings.Add(new WarningRecord($"Explicit retrieval filters applied: source_id={filters.SourceId ?? "(none)"}, document_id={filters.DocumentId ?? "(none)"}."));
        }
        if (contextWindow > 0)
        {
            warnings.Add(new WarningRecord($"Supporting context includes adjacent chunks with bounded window={contextWindow}; primary evidence remains the ranked match set."));
        }
        if (results.Count == 0 && filters.HasAny)
        {
            warnings.Add(new WarningRecord("Filter miss or filtered no-match; retrieval was not silently broadened."));
        }
        if (results.Count == 0 && !filters.HasAny)
        {
            warnings.Add(new WarningRecord("No lexical match found in the accessible corpus."));
        }
        if (results.Count > 1 && results[0].Score - results[1].Score <= 8)
        {
            warnings.Add(new WarningRecord("Top retrieval scores are close; treat ordering as weakly separated lexical evidence."));
        }
        if (results.Count > 0 && results[0].MatchedTerms.Count < terms.Count)
        {
            warnings.Add(new WarningRecord("Best result matched only part of the normalized query."));
        }
        if (results.Count > 0 && results[0].Score < 30)
        {
            warnings.Add(new WarningRecord("Weak lexical match; confidence is limited by low score."));
        }
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            warnings.Add(new WarningRecord("No session_id supplied; packet contains corpus evidence only unless session/state commands are used separately."));
        }
        else
        {
            warnings.Add(new WarningRecord($"Session/state evidence lane enabled for session_id={sessionId}; corpus and session evidence remain separate."));
        }
        if (operationalMemory.Length == 0)
        {
            warnings.Add(new WarningRecord("No matching structured operational memory was found for this query."));
        }
        else
        {
            warnings.Add(new WarningRecord("Structured operational memory included separately from corpus and session transcript evidence."));
            foreach (var operationalWarning in operationalMemory.Where(record => record.RecordType == "warning"))
            {
                warnings.Add(new WarningRecord($"Operational warning #{operationalWarning.RecordId}: {operationalWarning.Text}"));
            }
        }

        var confidence = CalculateConfidence(terms, results, sessionEvidence, operationalMemory, lifecycleFacts, retrievalPolicy);
        var plannerMetadata = new PlannerMetadata(
            planner.Mode.ToString().ToLowerInvariant(),
            planner.Status,
            keyName,
            planner.KeyFound,
            planner.EnvFileFound,
            retrievalQuery,
            planner.RefinedTerms,
            planner.Message,
            planner.Debug)
        {
            SemanticIntent = planner.Advice.SemanticIntent,
            ExpandedQueries = expandedQueries,
            ExpandedTerms = planner.Advice.ExpandedTerms,
            TargetLanes = planner.Advice.TargetLanes,
            RequiredEntities = planner.Advice.RequiredEntities,
            OptionalEntities = planner.Advice.OptionalEntities,
            LikelySessionScope = planner.Advice.LikelySessionScope,
            RetrievalStrategy = planner.Advice.RetrievalStrategy,
            PlannerConfidence = planner.Advice.Confidence,
            PlannerWarnings = planner.Advice.PlannerWarnings,
            ExpandedQueryDiagnostics = queryDiagnostics
        };

        return new ContextPacket(query, terms, confidence, facts, unknowns, warnings, sources, sourceIds, filters, contextWindow, results, supportingContext, sessionId, sessionEvidence, recentSessionContext, operationalMemory, retrievalPolicy, results, plannerMetadata);
    }

    private static IReadOnlyList<string> BuildExpandedQueries(string originalQuery, string retrievalQuery, PlannerAdvice advice)
    {
        return new[] { retrievalQuery }
            .Concat(advice.ExpandedQueries ?? Array.Empty<string>())
            .Append(originalQuery)
            .Select(query => string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(query => query.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildTerms(string retrievalQuery, PlannerAdvice advice)
    {
        return LexicalSearch.Tokenize(
                string.Join(' ', new[] { retrievalQuery }
                    .Concat(advice.ExpandedQueries ?? Array.Empty<string>())
                    .Concat(advice.ExpandedTerms ?? Array.Empty<string>())))
            .Take(25)
            .ToArray();
    }

    private IReadOnlyList<RetrievalResult> QueryCorpus(IReadOnlyList<string> queries, int limit, RetrievalFilters filters, List<ExpandedQueryDiagnostic> diagnostics)
    {
        var records = new Dictionary<string, RetrievalResult>(StringComparer.Ordinal);
        foreach (var query in queries)
        {
            var results = _store.Query(query, limit, filters);
            diagnostics.Add(new ExpandedQueryDiagnostic(query, results.Count, 0, 0, 0));
            foreach (var result in results)
            {
                if (!records.TryGetValue(result.ChunkId, out var existing) || result.Score > existing.Score)
                {
                    records[result.ChunkId] = result;
                }
            }
        }

        return records.Values
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.DocumentId, StringComparer.Ordinal)
            .ThenBy(result => result.ChunkIndex)
            .Take(limit)
            .Select((result, index) => result with { Rank = index + 1 })
            .ToArray();
    }

    private IReadOnlyList<SessionMessageEvidence> QuerySession(IReadOnlyList<string> queries, int limit, string sessionId, List<ExpandedQueryDiagnostic> diagnostics)
    {
        var records = new Dictionary<long, SessionMessageEvidence>();
        foreach (var query in queries)
        {
            var results = _store.QuerySessionMessages(sessionId, query, limit);
            UpdateDiagnostics(diagnostics, query, session: results.Count);
            foreach (var result in results)
            {
                if (!records.TryGetValue(result.MessageId, out var existing) || result.Score > existing.Score)
                {
                    records[result.MessageId] = result;
                }
            }
        }

        return records.Values
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.TimestampUtc)
            .Take(limit)
            .Select((result, index) => result with { Rank = index + 1 })
            .ToArray();
    }

    private IReadOnlyList<OperationalMemoryEvidence> QueryOperational(IReadOnlyList<string> queries, int limit, string? sessionId, List<ExpandedQueryDiagnostic> diagnostics)
    {
        var records = new Dictionary<long, OperationalMemoryEvidence>();
        foreach (var query in queries)
        {
            var results = _store.QueryOperationalRecords(query, limit, sessionId);
            UpdateDiagnostics(diagnostics, query, operational: results.Count);
            foreach (var result in results)
            {
                if (!records.TryGetValue(result.RecordId, out var existing) || result.Score > existing.Score)
                {
                    records[result.RecordId] = result;
                }
            }
        }

        return records.Values
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.CreatedUtc)
            .Take(limit)
            .Select((result, index) => result with { Rank = index + 1 })
            .ToArray();
    }

    private IReadOnlyList<FactRecord> QueryLifecycle(IReadOnlyList<string> queries, int limit, string? sessionId, List<ExpandedQueryDiagnostic> diagnostics)
    {
        var records = new Dictionary<int, FactRecord>();
        foreach (var query in queries)
        {
            var results = _store.QueryLifecycleFacts(query, limit, sessionId);
            UpdateDiagnostics(diagnostics, query, lifecycle: results.Count);
            foreach (var result in results)
            {
                records.TryAdd(result.FactId, result);
            }
        }

        return records.Values
            .OrderByDescending(result => result.CreatedUtc)
            .ThenByDescending(result => result.FactId)
            .Take(limit)
            .ToArray();
    }

    private static void UpdateDiagnostics(List<ExpandedQueryDiagnostic> diagnostics, string query, int session = 0, int operational = 0, int lifecycle = 0)
    {
        var index = diagnostics.FindIndex(item => string.Equals(item.Query, query, StringComparison.Ordinal));
        if (index < 0)
        {
            diagnostics.Add(new ExpandedQueryDiagnostic(query, 0, session, operational, lifecycle));
            return;
        }

        var current = diagnostics[index];
        diagnostics[index] = current with
        {
            SessionCandidates = current.SessionCandidates + session,
            OperationalCandidates = current.OperationalCandidates + operational,
            LifecycleCandidates = current.LifecycleCandidates + lifecycle
        };
    }

    private static string CalculateConfidence(
        IReadOnlyList<string> terms,
        IReadOnlyList<RetrievalResult> results,
        IReadOnlyList<SessionMessageEvidence> sessionEvidence,
        IReadOnlyList<OperationalMemoryEvidence> operationalMemory,
        IReadOnlyList<FactRecord> lifecycleFacts,
        RetrievalPolicySummary retrievalPolicy)
    {
        if (terms.Count == 0 || (results.Count == 0 && sessionEvidence.Count == 0 && operationalMemory.Count == 0 && lifecycleFacts.Count == 0))
        {
            return "none";
        }

        var topCorpus = results.Count == 0 ? 0 : results[0].Score;
        var topSession = sessionEvidence.Count == 0 ? 0 : sessionEvidence[0].Score;
        var topOperational = operationalMemory.Count == 0 ? 0 : operationalMemory[0].Score;
        var lifecycleScores = lifecycleFacts
            .Select(fact => LexicalSearch.Score(fact.Text, $"{fact.Status} {fact.SourceId} {fact.OriginIdentity}", terms))
            .OrderByDescending(score => score.Score)
            .ToArray();
        var topLifecycle = lifecycleScores.Length == 0 ? 0 : lifecycleScores[0].Score;
        var topMatchedTerms = results.Count > 0 && topCorpus >= topSession && topCorpus >= topOperational && topCorpus >= topLifecycle
            ? results[0].MatchedTerms.Count
            : sessionEvidence.Count > 0 && topSession >= topOperational && topSession >= topLifecycle
                ? sessionEvidence[0].MatchedTerms.Count
                : operationalMemory.Count > 0 && topOperational >= topLifecycle
                    ? operationalMemory[0].MatchedTerms.Count
                    : lifecycleScores.Length > 0 ? lifecycleScores[0].MatchedTerms.Count : 0;
        var topScore = Math.Max(Math.Max(topCorpus, topSession), Math.Max(topOperational, topLifecycle));
        var topPolicy = retrievalPolicy.Signals.FirstOrDefault();
        if (topPolicy is not null && topPolicy.LifecycleScore < 0)
        {
            return "very_low";
        }

        var coverage = topMatchedTerms / (double)terms.Count;
        if (coverage >= 1.0 && topScore >= 70)
        {
            return "medium";
        }

        if (coverage >= 0.5 && topScore >= 30)
        {
            return "low";
        }

        return "very_low";
    }
}
