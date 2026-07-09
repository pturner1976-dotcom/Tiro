namespace Tiro.Cli;

public static class RetrievalPolicy
{
    public const string CurrentState = "current-state";
    public const string Decision = "decision";
    public const string Unresolved = "unresolved";
    public const string Archive = "archive";
    public const string Lifecycle = "fact-lifecycle";
    public const string General = "general";

    private static readonly RetrievalModeDefinition[] ModeDefinitions =
    {
        new(Lifecycle, "matched lifecycle/status terms", NormalizeCandidates("conflict", "conflicting", "stale", "superseded", "lifecycle", "fact-lifecycle")),
        new(Unresolved, "matched unresolved work terms", NormalizeCandidates("todo", "todos", "unresolved", "blocked", "blocker", "open", "unknown", "warning", "warnings")),
        new(Decision, "matched decision/intent terms", NormalizeCandidates("decision", "decisions", "decided", "intent", "plan", "chosen", "choice")),
        new(Archive, "matched archive/reference terms", NormalizeCandidates("history", "historical", "archive", "archival", "reference", "document", "documents", "source", "sources", "provenance", "lookup")),
        new(CurrentState, "matched current-state terms", NormalizeCandidates("current", "now", "latest", "today", "active", "status", "state"))
    };

    public static RetrievalPolicySummary Build(
        IReadOnlyList<string> terms,
        IReadOnlyList<RetrievalResult> corpus,
        IReadOnlyList<SessionMessageEvidence> session,
        IReadOnlyList<OperationalMemoryEvidence> operational,
        IReadOnlyList<FactRecord> facts,
        RetrievalWeights? weights = null)
    {
        weights ??= RetrievalWeights.Default;
        var classification = Classify(terms);
        var timestamps = corpus.Select(item => (DateTimeOffset?)item.IngestedUtc)
            .Concat(session.Select(item => (DateTimeOffset?)item.TimestampUtc))
            .Concat(operational.Select(item => (DateTimeOffset?)item.CreatedUtc))
            .Concat(facts.Select(item => (DateTimeOffset?)item.CreatedUtc))
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToArray();
        var anchor = timestamps.Length == 0 ? DateTimeOffset.UtcNow : timestamps.Max();

        var signals = corpus.Select(item => ScoreCorpus(item, classification.Mode, anchor, weights))
            .Concat(session.Select(item => ScoreSession(item, classification.Mode, anchor, weights)))
            .Concat(operational.Select(item => ScoreOperational(item, classification.Mode, anchor, weights)))
            .Concat(facts.Select(item => ScoreFact(item, terms, classification.Mode, anchor, weights)))
            .OrderByDescending(item => item.FinalScore)
            .ThenByDescending(item => item.TimestampUtc)
            .ThenBy(item => item.EvidenceKey, StringComparer.Ordinal)
            .ToArray();

        return new RetrievalPolicySummary(classification.Mode, classification.Reason, classification.CompetingModes, signals);
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

    private static RetrievalClassification Classify(IReadOnlyList<string> terms)
    {
        var set = terms.ToHashSet(StringComparer.Ordinal);
        var matches = ModeDefinitions
            .Select((definition, precedence) => new
            {
                definition.Mode,
                definition.SingleModeReason,
                definition.Candidates,
                Precedence = precedence,
                MatchedTerms = definition.Candidates.Where(set.Contains).ToArray()
            })
            .Where(item => item.MatchedTerms.Length > 0)
            .Select(item => new RetrievalClassificationCandidate(
                item.Mode,
                item.SingleModeReason,
                item.Precedence,
                item.MatchedTerms))
            .ToArray();

        if (matches.Length == 0)
        {
            return new RetrievalClassification(General, "no mode-specific terms matched", Array.Empty<RetrievalModeMatch>());
        }

        var winner = matches
            .OrderByDescending(item => item.MatchedTerms.Count)
            .ThenBy(item => item.Precedence)
            .First();
        var competingModes = matches
            .Select(item => new RetrievalModeMatch(item.Mode, item.MatchedTerms))
            .ToArray();

        if (matches.Length == 1)
        {
            return new RetrievalClassification(winner.Mode, winner.SingleModeReason, competingModes);
        }

        var nextBest = matches
            .Where(item => !string.Equals(item.Mode, winner.Mode, StringComparison.Ordinal))
            .OrderByDescending(item => item.MatchedTerms.Count)
            .ThenBy(item => item.Precedence)
            .First();
        var tie = winner.MatchedTerms.Count == nextBest.MatchedTerms.Count;
        var reason = tie
            ? $"matched {winner.MatchedTerms.Count} {winner.Mode} terms; tie resolved by precedence over {nextBest.Mode}"
            : $"matched {winner.MatchedTerms.Count} {winner.Mode} terms (plurality over {nextBest.MatchedTerms.Count} {nextBest.Mode} term{(nextBest.MatchedTerms.Count == 1 ? string.Empty : "s")})";
        return new RetrievalClassification(winner.Mode, reason, competingModes);
    }

    private static RetrievalPolicySignal ScoreCorpus(RetrievalResult item, string mode, DateTimeOffset anchor, RetrievalWeights weights)
    {
        var relevance = item.Score;
        var recency = RecencyScore(item.IngestedUtc, anchor, weights);
        var importance = weights.CorpusImportance;
        var lifecycle = weights.CorpusLifecycle;
        var modeScore = mode switch
        {
            Archive => weights.CorpusArchiveModeBonus,
            CurrentState => weights.CorpusCurrentStateModePenalty,
            Decision => weights.CorpusDecisionModePenalty,
            Unresolved => weights.CorpusUnresolvedModePenalty,
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
            false,
            "normal",
            "ingested_utc",
            Explain(
                relevance,
                recency,
                importance,
                lifecycle,
                modeScore,
                "corpus",
                BuildRecencyWeightLabel(item.IngestedUtc, anchor, weights),
                $"CorpusImportance={weights.CorpusImportance}",
                $"CorpusLifecycle={weights.CorpusLifecycle}",
                mode switch
                {
                    Archive => $"CorpusArchiveModeBonus={weights.CorpusArchiveModeBonus}",
                    CurrentState => $"CorpusCurrentStateModePenalty={weights.CorpusCurrentStateModePenalty}",
                    Decision => $"CorpusDecisionModePenalty={weights.CorpusDecisionModePenalty}",
                    Unresolved => $"CorpusUnresolvedModePenalty={weights.CorpusUnresolvedModePenalty}",
                    _ => "CorpusMode=0"
                }));
    }

    private static RetrievalPolicySignal ScoreSession(SessionMessageEvidence item, string mode, DateTimeOffset anchor, RetrievalWeights weights)
    {
        var relevance = item.Score;
        var recency = RecencyScore(item.TimestampUtc, anchor, weights);
        var importance = weights.SessionImportance;
        var lifecycle = weights.SessionLifecycle;
        var modeScore = mode == CurrentState ? weights.SessionCurrentStateModeBonus : mode == Archive ? weights.SessionArchiveModePenalty : 0;
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
            false,
            "normal",
            "timestamp_utc",
            Explain(
                relevance,
                recency,
                importance,
                lifecycle,
                modeScore,
                "session",
                BuildRecencyWeightLabel(item.TimestampUtc, anchor, weights),
                $"SessionImportance={weights.SessionImportance}",
                $"SessionLifecycle={weights.SessionLifecycle}",
                mode == CurrentState
                    ? $"SessionCurrentStateModeBonus={weights.SessionCurrentStateModeBonus}"
                    : mode == Archive
                        ? $"SessionArchiveModePenalty={weights.SessionArchiveModePenalty}"
                        : "SessionMode=0"));
    }

    private static RetrievalPolicySignal ScoreOperational(OperationalMemoryEvidence item, string mode, DateTimeOffset anchor, RetrievalWeights weights)
    {
        var relevance = item.Score;
        var recency = RecencyScore(item.CreatedUtc, anchor, weights);
        var importance = item.RecordType switch
        {
            "decision" => weights.OperationalDecisionImportance,
            "warning" => weights.OperationalWarningImportance,
            "todo" => weights.OperationalTodoImportance,
            "unknown" => weights.OperationalUnknownImportance,
            _ => weights.OperationalDefaultImportance
        };
        var lifecycle = string.Equals(item.Status, "closed", StringComparison.Ordinal) ? weights.OperationalClosedLifecycle : weights.OperationalOpenLifecycle;
        var modeScore = mode switch
        {
            Decision when item.RecordType == "decision" => weights.OperationalDecisionModeBonus,
            Unresolved when item.RecordType is "todo" or "warning" or "unknown" => weights.OperationalUnresolvedModeBonus,
            CurrentState => weights.OperationalCurrentStateModeBonus,
            Archive => weights.OperationalArchiveModePenalty,
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
            item.ArchivedUtc.HasValue,
            item.RecordType,
            "created_utc",
            Explain(
                relevance,
                recency,
                importance,
                lifecycle,
                modeScore,
                "operational",
                BuildRecencyWeightLabel(item.CreatedUtc, anchor, weights),
                BuildOperationalImportanceWeightLabel(item.RecordType, weights),
                string.Equals(item.Status, "closed", StringComparison.Ordinal)
                    ? $"OperationalClosedLifecycle={weights.OperationalClosedLifecycle}"
                    : $"OperationalOpenLifecycle={weights.OperationalOpenLifecycle}",
                mode switch
                {
                    Decision when item.RecordType == "decision" => $"OperationalDecisionModeBonus={weights.OperationalDecisionModeBonus}",
                    Unresolved when item.RecordType is "todo" or "warning" or "unknown" => $"OperationalUnresolvedModeBonus={weights.OperationalUnresolvedModeBonus}",
                    CurrentState => $"OperationalCurrentStateModeBonus={weights.OperationalCurrentStateModeBonus}",
                    Archive => $"OperationalArchiveModePenalty={weights.OperationalArchiveModePenalty}",
                    _ => "OperationalMode=0"
                }));
    }

    private static RetrievalPolicySignal ScoreFact(FactRecord item, IReadOnlyList<string> terms, string mode, DateTimeOffset anchor, RetrievalWeights weights)
    {
        var lexicalScore = LexicalSearch.Score(item.Text, $"{item.Status} {item.SourceId} {item.OriginIdentity} {item.FreshnessHint ?? string.Empty}", terms);
        var relevance = lexicalScore.Score;
        var recency = RecencyScore(item.CreatedUtc, anchor, weights);
        var importance = ImportanceFromHint(item.FreshnessHint, weights);
        var lifecycle = LifecycleScore(item, anchor, weights);
        var modeScore = mode switch
        {
            Lifecycle => weights.FactLifecycleModeBonus,
            CurrentState when item.Status == TiroStore.FactStatus.Active => weights.FactCurrentStateActiveModeBonus,
            CurrentState => weights.FactCurrentStateNonActiveModePenalty,
            Archive => weights.FactArchiveModePenalty,
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
            item.ArchivedUtc.HasValue,
            item.FreshnessHint ?? "normal",
            item.ExpiresUtc.HasValue ? $"expires_utc={item.ExpiresUtc.Value:O}" : "created_utc",
            Explain(
                relevance,
                recency,
                importance,
                lifecycle,
                modeScore,
                "fact",
                BuildRecencyWeightLabel(item.CreatedUtc, anchor, weights),
                BuildImportanceWeightLabel(item.FreshnessHint, weights),
                BuildLifecycleWeightLabel(item, anchor, weights),
                mode switch
                {
                    Lifecycle => $"FactLifecycleModeBonus={weights.FactLifecycleModeBonus}",
                    CurrentState when item.Status == TiroStore.FactStatus.Active => $"FactCurrentStateActiveModeBonus={weights.FactCurrentStateActiveModeBonus}",
                    CurrentState => $"FactCurrentStateNonActiveModePenalty={weights.FactCurrentStateNonActiveModePenalty}",
                    Archive => $"FactArchiveModePenalty={weights.FactArchiveModePenalty}",
                    _ => "FactMode=0"
                }));
    }

    private static int RecencyScore(DateTimeOffset timestamp, DateTimeOffset anchor, RetrievalWeights weights)
    {
        var age = anchor - timestamp;
        if (age <= weights.RecencyWithinFiveMinutesMaxAge)
        {
            return weights.RecencyWithinFiveMinutesScore;
        }
        if (age <= weights.RecencyWithinTwentyFourHoursMaxAge)
        {
            return weights.RecencyWithinTwentyFourHoursScore;
        }
        if (age <= weights.RecencyWithinSevenDaysMaxAge)
        {
            return weights.RecencyWithinSevenDaysScore;
        }
        if (age <= weights.RecencyWithinThirtyDaysMaxAge)
        {
            return weights.RecencyWithinThirtyDaysScore;
        }

        return weights.RecencyOlderScore;
    }

    private static int ImportanceFromHint(string? hint, RetrievalWeights weights)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return weights.ImportanceDefault;
        }

        var normalized = hint.Trim().ToLowerInvariant();
        if (normalized.Contains("critical", StringComparison.Ordinal) || normalized.Contains("high", StringComparison.Ordinal) || normalized.Contains("important", StringComparison.Ordinal))
        {
            return weights.ImportanceHigh;
        }
        if (normalized.Contains("low", StringComparison.Ordinal))
        {
            return weights.ImportanceLow;
        }

        return weights.ImportanceDefault;
    }

    private static int LifecycleScore(FactRecord fact, DateTimeOffset anchor, RetrievalWeights weights)
    {
        if (fact.ExpiresUtc.HasValue && fact.ExpiresUtc.Value <= anchor)
        {
            return weights.LifecycleExpired;
        }

        return fact.Status switch
        {
            TiroStore.FactStatus.Active => weights.LifecycleActive,
            TiroStore.FactStatus.Unknown => weights.LifecycleUnknown,
            TiroStore.FactStatus.Stale => weights.LifecycleStale,
            TiroStore.FactStatus.Superseded => weights.LifecycleSuperseded,
            TiroStore.FactStatus.Conflicting => weights.LifecycleConflicting,
            _ => 0
        };
    }

    private static string BuildRecencyWeightLabel(DateTimeOffset timestamp, DateTimeOffset anchor, RetrievalWeights weights)
    {
        var age = anchor - timestamp;
        if (age <= weights.RecencyWithinFiveMinutesMaxAge)
        {
            return $"RecencyWithinFiveMinutesScore={weights.RecencyWithinFiveMinutesScore}";
        }
        if (age <= weights.RecencyWithinTwentyFourHoursMaxAge)
        {
            return $"RecencyWithinTwentyFourHoursScore={weights.RecencyWithinTwentyFourHoursScore}";
        }
        if (age <= weights.RecencyWithinSevenDaysMaxAge)
        {
            return $"RecencyWithinSevenDaysScore={weights.RecencyWithinSevenDaysScore}";
        }
        if (age <= weights.RecencyWithinThirtyDaysMaxAge)
        {
            return $"RecencyWithinThirtyDaysScore={weights.RecencyWithinThirtyDaysScore}";
        }

        return $"RecencyOlderScore={weights.RecencyOlderScore}";
    }

    private static string BuildImportanceWeightLabel(string? hint, RetrievalWeights weights)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return $"ImportanceDefault={weights.ImportanceDefault}";
        }

        var normalized = hint.Trim().ToLowerInvariant();
        if (normalized.Contains("critical", StringComparison.Ordinal) || normalized.Contains("high", StringComparison.Ordinal) || normalized.Contains("important", StringComparison.Ordinal))
        {
            return $"ImportanceHigh={weights.ImportanceHigh}";
        }
        if (normalized.Contains("low", StringComparison.Ordinal))
        {
            return $"ImportanceLow={weights.ImportanceLow}";
        }

        return $"ImportanceDefault={weights.ImportanceDefault}";
    }

    private static string BuildLifecycleWeightLabel(FactRecord fact, DateTimeOffset anchor, RetrievalWeights weights)
    {
        if (fact.ExpiresUtc.HasValue && fact.ExpiresUtc.Value <= anchor)
        {
            return $"LifecycleExpired={weights.LifecycleExpired}";
        }

        return fact.Status switch
        {
            TiroStore.FactStatus.Active => $"LifecycleActive={weights.LifecycleActive}",
            TiroStore.FactStatus.Unknown => $"LifecycleUnknown={weights.LifecycleUnknown}",
            TiroStore.FactStatus.Stale => $"LifecycleStale={weights.LifecycleStale}",
            TiroStore.FactStatus.Superseded => $"LifecycleSuperseded={weights.LifecycleSuperseded}",
            TiroStore.FactStatus.Conflicting => $"LifecycleConflicting={weights.LifecycleConflicting}",
            _ => "Lifecycle=0"
        };
    }

    private static string BuildOperationalImportanceWeightLabel(string recordType, RetrievalWeights weights)
    {
        return recordType switch
        {
            "decision" => $"OperationalDecisionImportance={weights.OperationalDecisionImportance}",
            "warning" => $"OperationalWarningImportance={weights.OperationalWarningImportance}",
            "todo" => $"OperationalTodoImportance={weights.OperationalTodoImportance}",
            "unknown" => $"OperationalUnknownImportance={weights.OperationalUnknownImportance}",
            _ => $"OperationalDefaultImportance={weights.OperationalDefaultImportance}"
        };
    }

    private static string Explain(
        int relevance,
        int recency,
        int importance,
        int lifecycle,
        int mode,
        string lane,
        string recencyWeight,
        string importanceWeight,
        string lifecycleWeight,
        string modeWeight)
    {
        return $"lane={lane}; final=relevance({relevance})+recency({recency} using {recencyWeight})+importance({importance} using {importanceWeight})+lifecycle({lifecycle} using {lifecycleWeight})+mode({mode} using {modeWeight})";
    }

    private static string[] NormalizeCandidates(params string[] values)
    {
        return values
            .SelectMany(LexicalSearch.Tokenize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private sealed record RetrievalModeDefinition(string Mode, string SingleModeReason, IReadOnlyList<string> Candidates);

    private sealed record RetrievalClassification(string Mode, string Reason, IReadOnlyList<RetrievalModeMatch> CompetingModes);

    private sealed record RetrievalClassificationCandidate(string Mode, string SingleModeReason, int Precedence, IReadOnlyList<string> MatchedTerms);
}
