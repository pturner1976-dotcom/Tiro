namespace Tiro.Cli;

public sealed class RetrievalDiagnostics
{
    public async Task<TiroSearchDiagnostics> SearchDebugAsync(
        TiroSearchDebugRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var queryResponse = await new TiroQueryService().QueryAsync(new TiroQueryRequest(
            request.DatabasePath,
            request.Query,
            request.Limit,
            request.SourceId,
            request.DocumentId,
            0,
            request.SessionId,
            request.PlannerMode,
            request.DebugPlanner,
            request.IncludeArchived), cancellationToken).ConfigureAwait(false);

        var packet = queryResponse.Packet;
        using var store = TiroStore.Open(queryResponse.DatabasePath);
        var stats = store.GetStats();

        var candidateCounts = new TiroSearchCandidateCounts(
            packet.PrimaryEvidence.Count,
            packet.SessionEvidence.Count,
            packet.OperationalMemory.Count,
            packet.Facts.Count(fact => fact.EvidenceType == "fact-lifecycle"));

        var matchedTerms = new TiroSearchMatchedTerms(
            DistinctTerms(packet.PrimaryEvidence.SelectMany(item => item.MatchedTerms)),
            DistinctTerms(packet.SessionEvidence.SelectMany(item => item.MatchedTerms)),
            DistinctTerms(packet.OperationalMemory.SelectMany(item => item.MatchedTerms)),
            DistinctTerms(packet.Facts
                .Where(fact => fact.EvidenceType == "fact-lifecycle")
                .SelectMany(fact => LexicalSearch.Score(
                    fact.Text,
                    $"{fact.Status} {fact.SourceId} {fact.OriginIdentity}",
                    packet.NormalizedTerms).MatchedTerms)));

        return new TiroSearchDiagnostics(
            queryResponse.DatabasePath,
            packet.Query,
            packet.NormalizedTerms,
            packet.RetrievalPolicy.QueryMode,
            packet.RetrievalPolicy.ModeReason,
            packet.RetrievalPolicy.CompetingModes,
            packet.Planner.Mode,
            packet.Planner.Status,
            packet.Planner.SemanticIntent,
            packet.Planner.RetrievalQuery,
            packet.Planner.ExpandedQueries,
            packet.Planner.ExpandedTerms,
            packet.Planner.TargetLanes,
            packet.Planner.RetrievalStrategy,
            packet.Planner.PlannerConfidence,
            packet.Planner.PlannerWarnings,
            packet.Planner.ExpandedQueryDiagnostics,
            packet.Filters.SourceId,
            packet.Filters.DocumentId,
            packet.SessionId,
            BuildLanesSearched(packet.SessionId),
            candidateCounts,
            matchedTerms,
            BuildTopScores(packet),
            stats,
            packet.Warnings.Select(warning => warning.Text).ToArray(),
            packet.Unknowns.Select(unknown => unknown.Text).ToArray());
    }

    private static IReadOnlyList<string> BuildLanesSearched(string? sessionId)
    {
        var lanes = new List<string> { "corpus", "operational", "lifecycle" };
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            lanes.Insert(1, "session");
        }

        return lanes;
    }

    private static IReadOnlyList<TiroSearchTopScore> BuildTopScores(ContextPacket packet)
    {
        var topScores = new List<TiroSearchTopScore>();
        var topCorpus = packet.PrimaryEvidence.FirstOrDefault();
        if (topCorpus is not null)
        {
            topScores.Add(new TiroSearchTopScore(
                "corpus",
                topCorpus.Score,
                topCorpus.MatchedTerms,
                topCorpus.ScoreSummary,
                FindPolicySignal(packet, "corpus")));
        }

        var topSession = packet.SessionEvidence.FirstOrDefault();
        if (topSession is not null)
        {
            topScores.Add(new TiroSearchTopScore(
                "session",
                topSession.Score,
                topSession.MatchedTerms,
                topSession.ScoreSummary,
                FindPolicySignal(packet, "session")));
        }

        var topOperational = packet.OperationalMemory.FirstOrDefault();
        if (topOperational is not null)
        {
            topScores.Add(new TiroSearchTopScore(
                "operational",
                topOperational.Score,
                topOperational.MatchedTerms,
                topOperational.ScoreSummary,
                FindPolicySignal(packet, "operational")));
        }

        var topLifecycle = packet.Facts.FirstOrDefault(fact => fact.EvidenceType == "fact-lifecycle");
        if (topLifecycle is not null)
        {
            var score = LexicalSearch.Score(
                topLifecycle.Text,
                $"{topLifecycle.Status} {topLifecycle.SourceId} {topLifecycle.OriginIdentity}",
                packet.NormalizedTerms);
            topScores.Add(new TiroSearchTopScore(
                "lifecycle",
                score.Score,
                score.MatchedTerms,
                score.Summary,
                FindPolicySignal(packet, "fact-lifecycle")));
        }

        return topScores
            .OrderByDescending(score => score.Score)
            .ThenBy(score => score.Lane, StringComparer.Ordinal)
            .ToArray();
    }

    private static RetrievalPolicySignal? FindPolicySignal(ContextPacket packet, string evidenceType)
    {
        return packet.RetrievalPolicy.Signals.FirstOrDefault(
            signal => signal.EvidenceType.Equals(evidenceType, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> DistinctTerms(IEnumerable<string> terms)
    {
        return terms
            .Distinct(StringComparer.Ordinal)
            .OrderBy(term => term, StringComparer.Ordinal)
            .ToArray();
    }
}
