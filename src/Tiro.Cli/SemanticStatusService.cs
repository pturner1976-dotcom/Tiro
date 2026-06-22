namespace Tiro.Cli;

/// <summary>Read-only semantic config + index-state report. Never makes a provider call.</summary>
public sealed class SemanticStatusService
{
    public SemanticStatusResponse GetStatus(string databasePath, EmbeddingConfig config)
    {
        using var store = TiroStore.Open(databasePath);
        var stats = store.GetEmbeddingStats();
        var lastRun = store.GetLastIndexRun();
        var warnings = new List<string>();

        if (!config.Enabled)
        {
            warnings.Add("Semantic search is disabled (TIRO_SEMANTIC_ENABLED is not \"true\").");
        }
        else if (config.Provider == "none")
        {
            warnings.Add("Semantic search is enabled but TIRO_EMBEDDING_PROVIDER is \"none\" or unset.");
        }
        else if (!config.KeyFound)
        {
            warnings.Add($"Semantic search is enabled but {config.KeyName} is not set.");
        }

        return new SemanticStatusResponse(
            "ok",
            config.Enabled,
            config.Provider,
            config.Model,
            config.KeyFound ? "(set, redacted)" : "(not set)",
            stats.EmbeddingCount,
            stats.ActiveEmbeddingCount,
            stats.IndexedLanes,
            lastRun,
            warnings,
            Array.Empty<string>());
    }
}
