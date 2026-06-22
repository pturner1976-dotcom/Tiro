namespace Tiro.Cli;

/// <summary>
/// Standalone vector-similarity search over stored semantic_embeddings.
/// Never replaces lexical search — see Program.cs for the separate `query`/
/// `recall`/`phrase-search` commands, which are entirely untouched by this
/// class. See docs/WP4C_NATIVE_SEMANTIC_ENGINE_DESIGN.md.
/// </summary>
public sealed class SemanticSearchService
{
    public async Task<SemanticQueryResponse> SearchAsync(
        string databasePath,
        string query,
        int limit,
        double minScore,
        IReadOnlyList<string> lanes,
        EmbeddingConfig config,
        IEmbeddingClient? client,
        CancellationToken cancellationToken)
    {
        if (!config.Enabled)
        {
            return new SemanticQueryResponse(
                "disabled",
                query,
                false,
                Array.Empty<SemanticHit>(),
                new[] { "Semantic search is disabled (TIRO_SEMANTIC_ENABLED is not \"true\"); no provider call was made." },
                Array.Empty<string>());
        }

        if (client is null)
        {
            return new SemanticQueryResponse(
                "validation_error",
                query,
                true,
                Array.Empty<SemanticHit>(),
                Array.Empty<string>(),
                new[] { $"Embedding provider ({config.Provider}) key ({config.KeyName}) is not configured; no provider call was made." });
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return new SemanticQueryResponse("validation_error", query, true, Array.Empty<SemanticHit>(), Array.Empty<string>(), new[] { "Query text is required." });
        }

        using var store = TiroStore.Open(databasePath);

        EmbeddingResult queryEmbedding;
        try
        {
            queryEmbedding = await client.EmbedAsync(query.Trim(), cancellationToken).ConfigureAwait(false);
        }
        catch (EmbeddingClientException ex)
        {
            return new SemanticQueryResponse("process_error", query, true, Array.Empty<SemanticHit>(), Array.Empty<string>(), new[] { ex.Message });
        }

        var stored = store.ListActiveEmbeddings(config.Provider, config.Model, lanes);
        var warnings = new List<string>();
        var scored = new List<(StoredEmbedding Embedding, double Score)>();
        foreach (var item in stored)
        {
            try
            {
                var vector = SemanticVectorCodec.Decode(item.VectorBlob, item.Dimensions);
                var score = SemanticVectorCodec.CosineSimilarity(queryEmbedding.Vector, vector);
                if (score >= minScore)
                {
                    scored.Add((item, score));
                }
            }
            catch (InvalidOperationException ex)
            {
                warnings.Add($"Skipped unreadable embedding {item.EmbeddingId}: {ex.Message}");
            }
        }

        var ranked = scored.OrderByDescending(entry => entry.Score).Take(limit).ToList();
        var hits = ranked.Select((entry, index) => new SemanticHit(
            index + 1,
            Math.Round(entry.Score, 4),
            entry.Embedding.TargetLane,
            entry.Embedding.TargetKind,
            entry.Embedding.TargetId,
            entry.Embedding.SourceId,
            entry.Embedding.DocumentId,
            entry.Embedding.ChunkId,
            entry.Embedding.SessionId,
            entry.Embedding.MessageId,
            entry.Embedding.Snippet,
            entry.Embedding.TextHash,
            entry.Embedding.EmbeddingProvider,
            entry.Embedding.EmbeddingModel,
            $"No lexical terms required; surfaced by semantic similarity {Math.Round(entry.Score, 4)} (>= threshold {minScore})."))
            .ToList();

        if (hits.Count == 0 && warnings.Count == 0)
        {
            warnings.Add("No stored embedding scored at or above the minimum similarity threshold.");
        }

        return new SemanticQueryResponse("ok", query, true, hits, warnings, Array.Empty<string>());
    }
}
