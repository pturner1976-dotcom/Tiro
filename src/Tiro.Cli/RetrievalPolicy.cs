namespace Tiro.Cli;

public static class RetrievalPolicy
{
    public const string CurrentState = "current-state";
    public const string Decision = "decision";
    public const string Unresolved = "unresolved";
    public const string Archive = "archive";
    public const string Lifecycle = "fact-lifecycle";
    public const string General = "general";

    public static RetrievalPolicySummary Build(
        IReadOnlyList<string> terms,
        IReadOnlyList<RetrievalResult> corpus,
        IReadOnlyList<SessionMessageEvidence> session,
        IReadOnlyList<OperationalMemoryEvidence> operational,
        IReadOnlyList<FactRecord> facts)
    {
        var classification = Classify(terms);
        var timestamps = corpus.Select(item => (DateTimeOffset?)item.IngestedUtc)
            .Concat(session.Select(item => (DateTimeOffset?)item.TimestampUtc))
            .Concat(operational.Select(item => (DateTimeOffset?)item.CreatedUtc))
            .Concat(facts.Select(item => (DateTimeOffset?)item.CreatedUtc))
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToArray();
        var anchor = timestamps.Length == 0 ? DateTimeOffset.UtcNow : timestamps.Max();

        var signals = corpus.Select(item => ScoreCorpus(item, classification.Mode, anchor))
            .Concat(session.Select(item => ScoreSession(item, classification.Mode, anchor)))
            .Concat(operational.Select(item => ScoreOperational(item, classification.Mode, anchor)))
            .Concat(facts.Select(item => ScoreFact(item, terms, classification.Mode, anchor)))
            .OrderByDescending(item => item.FinalScore)
            .ThenByDescending(item => item.TimestampUtc)
            .ThenBy(item => item.EvidenceKey, StringComparer.Ordinal)
            .ToArray();

        return new RetrievalPolicySummary(classification.Mode, classification.Reason, signals);
    }

    public static RetrievalPolicySignal? FindSignal(RetrievalPolicySummary policy, FactRecord fact)
    {
        var key = FactKey(fact);
        return policy.Signals.FirstOrDefault(signal => string.Equals(signal.EvidenceKey, key, StringComparison.Ordinal));
    }

    public static string FactKey(FactRecord fact)
    {
        return fact.EvidenceType switch
        {
            "corpus" when fact.LinkedSourceIds.Count > 0 => $"corpus|{fact.LinkedSourceIds[0]}",
            "corpus" => $"corpus|{fact.SourceId}|{fact.Text}",
            "session" when fact.LinkedMessageIds.Count > 0 => $"session|{fact.SessionId ?? string.Empty}|{fact.LinkedMessageIds[0]}",
            "session" => $"session|{fact.SessionId ?? string.Empty}|{fact.Text}",
            "operational" when fact.LinkedRecordIds.Count > 0 => $"operational|{fact.LinkedRecordIds[0]}",
            "operational" => $"operational|{fact.Text}",
            _ => $"fact|{fact.FactId}"
        };
    }

    private static (string Mode, string Reason) Classify(IReadOnlyList<string> terms)
    {
        var set = terms.ToHashSet(StringComparer.Ordinal);
        if (ContainsAny(set, "conflict", "conflicting", "stale", "superseded", "lifecycle", "fact-lifecycle"))
        {
            return (Lifecycle, "matched lifecycle/status terms");
        }

        if (ContainsAny(set, "todo", "todos", "unresolved", "blocked", "blocker", "open", "unknown", "warning", "warnings"))
        {
            return (Unresolved, "matched unresolved work terms");
        }

        if (ContainsAny(set, "decision", "decisions", "decided", "intent", "plan", "chosen", "choice"))
        {
            return (Decision, "matched decision/intent terms");
        }

        if (ContainsAny(set, "history", "historical", "archive", "archival", "reference", "document", "documents", "source", "sources", "provenance", "lookup"))
        {
            return (Archive, "matched archive/reference terms");
        }

        if (ContainsAny(set, "current", "now", "latest", "today", "active", "status", "state"))
        {
            return (CurrentState, "matched current-state terms");
        }

        return (General, "no mode-specific terms matched");
    }

    private static RetrievalPolicySignal ScoreCorpus(RetrievalResult item, string mode, DateTimeOffset anchor)
    {
        var relevance = item.Score;
        var recency = RecencyScore(item.IngestedUtc, anchor);
        const int importance = 10;
        const int lifecycle = 20;
        var modeScore = mode switch
        {
            Archive => 35,
            CurrentState => -10,
            Decision or Unresolved => -10,
            _ => 0
        };
        var final = relevance + recency + importance + lifecycle + modeScore;
        return new RetrievalPolicySignal(
            $"corpus|{item.ChunkId}",
            "corpus",
            item.ChunkId,
            relevance,
            recency,
            importance,
            lifecycle,
            modeScore,
            final,
            item.IngestedUtc,
            "active",
            "normal",
            "ingested_utc",
            Explain(relevance, recency, importance, lifecycle, modeScore));
    }

    private static RetrievalPolicySignal ScoreSession(SessionMessageEvidence item, string mode, DateTimeOffset anchor)
    {
        var relevance = item.Score;
        var recency = RecencyScore(item.TimestampUtc, anchor);
        const int importance = 20;
        const int lifecycle = 20;
        var modeScore = mode == CurrentState ? 30 : mode == Archive ? -15 : 0;
        var final = relevance + recency + importance + lifecycle + modeScore;
        return new RetrievalPolicySignal(
            $"session|{item.SessionId}|{item.MessageId}",
            "session",
            $"message:{item.MessageId}",
            relevance,
            recency,
            importance,
            lifecycle,
            modeScore,
            final,
            item.TimestampUtc,
            "active",
            "normal",
            "timestamp_utc",
            Explain(relevance, recency, importance, lifecycle, modeScore));
    }

    private static RetrievalPolicySignal ScoreOperational(OperationalMemoryEvidence item, string mode, DateTimeOffset anchor)
    {
        var relevance = item.Score;
        var recency = RecencyScore(item.CreatedUtc, anchor);
        var importance = item.RecordType switch
        {
            "decision" => 40,
            "warning" => 40,
            "todo" => 35,
            "unknown" => 20,
            _ => 15
        };
        var lifecycle = string.Equals(item.Status, "closed", StringComparison.Ordinal) ? 5 : 25;
        var modeScore = mode switch
        {
            Decision when item.RecordType == "decision" => 80,
            Unresolved when item.RecordType is "todo" or "warning" or "unknown" => 70,
            CurrentState => 10,
            Archive => -20,
            _ => 0
        };
        var final = relevance + recency + importance + lifecycle + modeScore;
        return new RetrievalPolicySignal(
            $"operational|{item.RecordId}",
            "operational",
            $"{item.RecordType}:{item.RecordId}",
            relevance,
            recency,
            importance,
            lifecycle,
            modeScore,
            final,
            item.CreatedUtc,
            item.Status,
            item.RecordType,
            "created_utc",
            Explain(relevance, recency, importance, lifecycle, modeScore));
    }

    private static RetrievalPolicySignal ScoreFact(FactRecord item, IReadOnlyList<string> terms, string mode, DateTimeOffset anchor)
    {
        var lexicalScore = LexicalSearch.Score(item.Text, $"{item.Status} {item.SourceId} {item.OriginIdentity} {item.FreshnessHint ?? string.Empty}", terms);
        var relevance = lexicalScore.Score;
        var recency = RecencyScore(item.CreatedUtc, anchor);
        var importance = ImportanceFromHint(item.FreshnessHint);
        var lifecycle = LifecycleScore(item, anchor);
        var modeScore = mode switch
        {
            Lifecycle => 35,
            CurrentState when item.Status == TiroStore.FactStatus.Active => 25,
            CurrentState => -10,
            Archive => -5,
            _ => 0
        };
        var final = relevance + recency + importance + lifecycle + modeScore;
        return new RetrievalPolicySignal(
            FactKey(item),
            item.EvidenceType,
            item.FactId > 0 ? $"fact:{item.FactId}" : item.EvidenceType,
            relevance,
            recency,
            importance,
            lifecycle,
            modeScore,
            final,
            item.CreatedUtc,
            item.Status,
            item.FreshnessHint ?? "normal",
            item.ExpiresUtc.HasValue ? $"expires_utc={item.ExpiresUtc.Value:O}" : "created_utc",
            Explain(relevance, recency, importance, lifecycle, modeScore));
    }

    private static int RecencyScore(DateTimeOffset timestamp, DateTimeOffset anchor)
    {
        var age = anchor - timestamp;
        if (age <= TimeSpan.FromMinutes(5))
        {
            return 40;
        }
        if (age <= TimeSpan.FromHours(24))
        {
            return 35;
        }
        if (age <= TimeSpan.FromDays(7))
        {
            return 25;
        }
        if (age <= TimeSpan.FromDays(30))
        {
            return 15;
        }

        return 5;
    }

    private static int ImportanceFromHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return 25;
        }

        var normalized = hint.Trim().ToLowerInvariant();
        if (normalized.Contains("critical", StringComparison.Ordinal) || normalized.Contains("high", StringComparison.Ordinal) || normalized.Contains("important", StringComparison.Ordinal))
        {
            return 45;
        }
        if (normalized.Contains("low", StringComparison.Ordinal))
        {
            return 10;
        }

        return 25;
    }

    private static int LifecycleScore(FactRecord fact, DateTimeOffset anchor)
    {
        if (fact.ExpiresUtc.HasValue && fact.ExpiresUtc.Value <= anchor)
        {
            return -40;
        }

        return fact.Status switch
        {
            TiroStore.FactStatus.Active => 30,
            TiroStore.FactStatus.Unknown => 5,
            TiroStore.FactStatus.Stale => -20,
            TiroStore.FactStatus.Superseded => -40,
            TiroStore.FactStatus.Conflicting => -30,
            _ => 0
        };
    }

    private static bool ContainsAny(HashSet<string> terms, params string[] candidates)
    {
        return candidates.Any(terms.Contains);
    }

    private static string Explain(int relevance, int recency, int importance, int lifecycle, int mode)
    {
        return $"final=relevance({relevance})+recency({recency})+importance({importance})+lifecycle({lifecycle})+mode({mode})";
    }
}
