namespace Tiro.Cli;

/// <summary>
/// Merges lexical (corpus) retrieval with semantic retrieval by target
/// identity, exposing both component scores plus a combined score. Lexical
/// search itself is untouched — this only reads its results via
/// TiroStore.Query, the same method the existing `query` command uses.
///
/// Scope note: the lexical side of this merge covers the corpus lane only.
/// store.Query already implements exact-phrase-aware ranked lexical scoring
/// for corpus chunks; a general cross-session lexical ranking equivalent
/// does not exist in this codebase (session-query is scoped to one
/// session at a time) and building one was out of scope for this pass — see
/// docs/WP4C_NATIVE_SEMANTIC_ENGINE_REPORT.md. The semantic side still
/// covers every requested lane.
/// </summary>
public sealed class HybridSearchService
{
    private const double LexicalNormalizationCeiling = 150.0;
    private const double DefaultLexicalWeight = 0.55;
    private const double DefaultSemanticWeight = 0.45;

    public async Task<HybridSearchResponse> SearchAsync(
        string databasePath,
        string query,
        int limit,
        IReadOnlyList<string> lanes,
        double lexicalWeight,
        double semanticWeight,
        double minSemanticScore,
        EmbeddingConfig config,
        IEmbeddingClient? client,
        CancellationToken cancellationToken)
    {
        using var store = TiroStore.Open(databasePath);
        var warnings = new List<string>();

        var lexicalResults = lanes.Contains("corpus") && !string.IsNullOrWhiteSpace(query)
            ? store.Query(query, Math.Max(limit * 3, 20))
            : Array.Empty<RetrievalResult>();
        var lexicalByKey = lexicalResults.ToDictionary(r => ("corpus", "chunk", r.ChunkId));

        var semanticByKey = new Dictionary<(string Lane, string Kind, string Id), (StoredEmbedding Embedding, double Score)>();
        var semanticParticipated = false;

        if (!config.Enabled)
        {
            warnings.Add("Semantic disabled (TIRO_SEMANTIC_ENABLED is not \"true\"); hybrid search ran lexical-only.");
        }
        else if (client is null)
        {
            warnings.Add($"Embedding provider key ({config.KeyName}) is not configured; hybrid search ran lexical-only.");
        }
        else
        {
            try
            {
                var queryEmbedding = await client.EmbedAsync(query.Trim(), cancellationToken).ConfigureAwait(false);
                foreach (var item in store.ListActiveEmbeddings(config.Provider, config.Model, lanes))
                {
                    try
                    {
                        var vector = SemanticVectorCodec.Decode(item.VectorBlob, item.Dimensions);
                        var score = SemanticVectorCodec.CosineSimilarity(queryEmbedding.Vector, vector);
                        if (score >= minSemanticScore)
                        {
                            semanticByKey[(item.TargetLane, item.TargetKind, item.TargetId)] = (item, score);
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        warnings.Add($"Skipped unreadable embedding {item.EmbeddingId}: {ex.Message}");
                    }
                }

                semanticParticipated = true;
            }
            catch (EmbeddingClientException ex)
            {
                warnings.Add($"Semantic search failed ({ex.Message}); hybrid search fell back to lexical-only.");
            }
        }

        var keys = new HashSet<(string Lane, string Kind, string Id)>(lexicalByKey.Keys);
        keys.UnionWith(semanticByKey.Keys);

        var hits = new List<HybridHit>();
        var protectedKeys = new HashSet<(string Lane, string Kind, string Id)>(
            lexicalResults.Where(r => r.ScoreSummary.PhraseBonus > 0).Select(r => ("corpus", "chunk", r.ChunkId)));

        foreach (var key in keys)
        {
            lexicalByKey.TryGetValue(key, out var lexical);
            var hasSemantic = semanticByKey.TryGetValue(key, out var semanticEntry);

            double? lexicalNormalized = lexical is null ? null : Math.Min(1.0, lexical.Score / LexicalNormalizationCeiling);
            double? semanticScore = hasSemantic ? semanticEntry.Score : null;
            var combined = (lexicalNormalized ?? 0) * lexicalWeight + (semanticScore ?? 0) * semanticWeight;

            var explanation = (lexical is not null, hasSemantic) switch
            {
                (true, true) => $"Matched lexically (raw score {lexical!.Score}, phrase_bonus {lexical.ScoreSummary.PhraseBonus}); semantic score also {semanticScore:F2}.",
                (true, false) => protectedKeys.Contains(key)
                    ? $"Exact-phrase lexical match (phrase_bonus {lexical!.ScoreSummary.PhraseBonus}); protected from being buried by semantic-only ranking."
                    : $"Matched lexically (raw score {lexical!.Score}); no semantic comparison met the threshold.",
                (false, true) => $"No lexical terms matched; surfaced by semantic similarity {semanticScore:F2} above threshold {minSemanticScore}.",
                _ => "Unexpected: neither lexical nor semantic signal present.",
            };

            hits.Add(new HybridHit(
                0,
                Math.Round(combined, 4),
                lexicalNormalized is null ? null : Math.Round(lexicalNormalized.Value, 4),
                semanticScore is null ? null : Math.Round(semanticScore.Value, 4),
                lexical?.MatchedTerms ?? Array.Empty<string>(),
                key.Lane,
                key.Kind,
                key.Id,
                lexical?.SourceId ?? semanticEntry.Embedding?.SourceId,
                lexical?.DocumentId ?? semanticEntry.Embedding?.DocumentId,
                key.Kind == "chunk" ? key.Id : null,
                semanticEntry.Embedding?.SessionId,
                explanation));
        }

        var rankedByScore = hits.OrderByDescending(h => h.CombinedScore).ToList();
        var topSlice = rankedByScore.Take(limit).ToList();
        var topKeys = new HashSet<(string, string, string)>(topSlice.Select(h => (h.TargetLane, h.TargetKind, h.TargetId)));

        // Exact-phrase protection: any protected lexical hit must appear in
        // the final result even if its combined score would have placed it
        // outside the requested limit.
        foreach (var hit in rankedByScore)
        {
            var key = (hit.TargetLane, hit.TargetKind, hit.TargetId);
            if (protectedKeys.Contains(key) && !topKeys.Contains(key))
            {
                topSlice.Add(hit);
                topKeys.Add(key);
            }
        }

        var finalHits = topSlice
            .OrderByDescending(h => h.CombinedScore)
            .Select((hit, index) => hit with { Rank = index + 1 })
            .ToList();

        return new HybridSearchResponse("ok", query, semanticParticipated, finalHits, warnings, Array.Empty<string>());
    }
}
