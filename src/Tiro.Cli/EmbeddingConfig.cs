namespace Tiro.Cli;

/// <summary>
/// Semantic search configuration, read from process environment variables
/// only (unlike PlannerConfig, there is no legacy env-file fallback to
/// preserve here). See docs/WP4C_NATIVE_SEMANTIC_ENGINE_DESIGN.md for the
/// config-gating rule this enforces: no provider call may happen anywhere
/// unless Enabled is true, and every command checks Enabled before
/// constructing or calling an IEmbeddingClient at all.
/// </summary>
public sealed record EmbeddingConfig(
    bool Enabled,
    string Provider,
    string Model,
    string KeyName,
    bool KeyFound,
    string BaseUrl,
    double MinScore,
    int MaxResults,
    int IndexBatchSize)
{
    public const string DefaultProvider = "none";
    public const string DefaultModel = "text-embedding-3-small";
    private const string DefaultBaseUrl = "https://api.openai.com/v1";

    // True when the provider can be used — key found for openai, always true for local.
    public bool IsReady => Provider == "local" || KeyFound;

    public static EmbeddingConfig Load()
    {
        var enabled = string.Equals(
            Environment.GetEnvironmentVariable("TIRO_SEMANTIC_ENABLED"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var provider = NormalizeProvider(Environment.GetEnvironmentVariable("TIRO_EMBEDDING_PROVIDER"));
        var model = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TIRO_EMBEDDING_MODEL"))
            ? DefaultModel
            : Environment.GetEnvironmentVariable("TIRO_EMBEDDING_MODEL")!.Trim();
        var keyName = provider == "openai" ? "OPENAI_API_KEY" : "NONE";
        var keyFound = provider == "openai" && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(keyName));
        var baseUrl = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TIRO_EMBEDDING_BASE_URL"))
            ? DefaultBaseUrl
            : Environment.GetEnvironmentVariable("TIRO_EMBEDDING_BASE_URL")!.Trim();
        // 0.35 is calibrated empirically (see docs/WP4C_NATIVE_SEMANTIC_ENGINE_REPORT.md),
        // not the contract's originally-specified 0.70. With OpenAI
        // text-embedding-3-small on short evidence-style text, real
        // zero-lexical-overlap paraphrase matches scored ~0.51 and a
        // genuinely unrelated negative control scored ~0.11 — a 0.70
        // default would have silently returned zero results for every
        // realistic query, which is worse than a slightly loose default.
        var minScore = ParseDouble(Environment.GetEnvironmentVariable("TIRO_SEMANTIC_MIN_SCORE"), 0.35);
        var maxResults = ParseInt(Environment.GetEnvironmentVariable("TIRO_SEMANTIC_MAX_RESULTS"), 20);
        var batchSize = ParseInt(Environment.GetEnvironmentVariable("TIRO_SEMANTIC_INDEX_BATCH_SIZE"), 64);

        return new EmbeddingConfig(enabled, provider, model, keyName, keyFound, baseUrl, minScore, maxResults, batchSize);
    }

    public string? GetApiKey()
    {
        return Provider == "openai" ? Environment.GetEnvironmentVariable(KeyName) : null;
    }

    private static string NormalizeProvider(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "openai" or "local" or "none" ? normalized : DefaultProvider;
    }

    private static double ParseDouble(string? value, double fallback) =>
        !string.IsNullOrWhiteSpace(value) && double.TryParse(value, out var parsed) ? parsed : fallback;

    private static int ParseInt(string? value, int fallback) =>
        !string.IsNullOrWhiteSpace(value) && int.TryParse(value, out var parsed) ? parsed : fallback;
}
