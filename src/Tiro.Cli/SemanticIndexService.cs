namespace Tiro.Cli;

/// <summary>
/// Generates and stores embeddings for corpus chunks and session messages.
/// Dry-run reports candidates and skip/reindex decisions without ever
/// constructing or calling an IEmbeddingClient. A live run requires
/// EmbeddingConfig.Enabled and a resolved client — callers are responsible
/// for that gate (Program.cs), this service trusts it was already checked
/// for the live path but re-derives candidate/skip counts independently of
/// it for the dry-run path. See docs/WP4C_NATIVE_SEMANTIC_ENGINE_DESIGN.md.
/// </summary>
public sealed class SemanticIndexService
{
    public async Task<SemanticIndexResponse> IndexAsync(
        string databasePath,
        IReadOnlyList<string> lanes,
        bool rebuild,
        bool dryRun,
        int? limit,
        EmbeddingConfig config,
        IEmbeddingClient? client,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("n");
        var startedUtc = DateTimeOffset.UtcNow;
        var errors = new List<string>();
        var seen = 0;
        var indexed = 0;
        var skipped = 0;
        var failed = 0;

        using var store = TiroStore.Open(databasePath);

        var candidates = new List<EmbeddingTargetCandidate>();
        if (lanes.Contains("corpus"))
        {
            candidates.AddRange(store.ListCorpusChunksForEmbedding());
        }
        if (lanes.Contains("session"))
        {
            candidates.AddRange(store.ListSessionMessagesForEmbedding(null));
        }

        if (limit.HasValue)
        {
            candidates = candidates.Take(limit.Value).ToList();
        }

        foreach (var candidate in candidates)
        {
            seen++;
            var textHash = TiroStore.ComputeTextHash(candidate.Text);

            if (!rebuild)
            {
                var existingHash = store.GetActiveEmbeddingTextHash(config.Provider, config.Model, candidate.TargetLane, candidate.TargetKind, candidate.TargetId);
                if (existingHash == textHash)
                {
                    skipped++;
                    continue;
                }
            }

            if (dryRun)
            {
                indexed++;
                continue;
            }

            try
            {
                var result = await client!.EmbedAsync(candidate.Text, cancellationToken).ConfigureAwait(false);
                store.UpsertEmbedding(candidate, config.Provider, config.Model, textHash, result.Vector, DateTimeOffset.UtcNow);
                indexed++;
            }
            catch (Exception ex) when (ex is EmbeddingClientException or InvalidOperationException)
            {
                failed++;
                errors.Add($"{candidate.TargetLane}/{candidate.TargetKind}/{candidate.TargetId}: {ex.Message}");
            }
        }

        var completedUtc = DateTimeOffset.UtcNow;
        var status = dryRun ? "dry_run" : failed > 0 ? "completed_with_errors" : "ok";
        var run = new SemanticIndexRunSummary(
            runId,
            startedUtc,
            completedUtc,
            config.Provider,
            config.Model,
            string.Join(",", lanes),
            seen,
            indexed,
            skipped,
            failed,
            dryRun,
            status);

        store.RecordIndexRun(run);
        return new SemanticIndexResponse(status, run, errors);
    }
}
