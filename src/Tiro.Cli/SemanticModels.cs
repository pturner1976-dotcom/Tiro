using System.Text.Json.Serialization;

namespace Tiro.Cli;

/// <summary>A record eligible for embedding, resolved from an existing storage lane.</summary>
public sealed record EmbeddingTargetCandidate(
    string TargetLane,
    string TargetKind,
    string TargetId,
    string? SourceId,
    string? DocumentId,
    string? ChunkId,
    string? SessionId,
    long? MessageId,
    string Text);

public sealed record StoredEmbedding(
    string EmbeddingId,
    string TargetLane,
    string TargetKind,
    string TargetId,
    string? SourceId,
    string? DocumentId,
    string? ChunkId,
    string? SessionId,
    long? MessageId,
    string TextHash,
    string EmbeddingProvider,
    string EmbeddingModel,
    int Dimensions,
    byte[] VectorBlob,
    string Snippet);

public sealed record SemanticEmbeddingStats(
    [property: JsonPropertyName("embedding_count")] int EmbeddingCount,
    [property: JsonPropertyName("active_embedding_count")] int ActiveEmbeddingCount,
    [property: JsonPropertyName("indexed_lanes")] IReadOnlyList<string> IndexedLanes);

public sealed record SemanticIndexRunSummary(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("started_utc")] DateTimeOffset StartedUtc,
    [property: JsonPropertyName("completed_utc")] DateTimeOffset? CompletedUtc,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("target_lanes")] string TargetLanes,
    [property: JsonPropertyName("records_seen")] int RecordsSeen,
    [property: JsonPropertyName("records_indexed")] int RecordsIndexed,
    [property: JsonPropertyName("records_skipped")] int RecordsSkipped,
    [property: JsonPropertyName("records_failed")] int RecordsFailed,
    [property: JsonPropertyName("dry_run")] bool DryRun,
    [property: JsonPropertyName("status")] string Status);

public sealed record SemanticStatusResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("semantic_enabled")] bool SemanticEnabled,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("provider_key_present_redacted")] string ProviderKeyPresentRedacted,
    [property: JsonPropertyName("embedding_count")] int EmbeddingCount,
    [property: JsonPropertyName("active_embedding_count")] int ActiveEmbeddingCount,
    [property: JsonPropertyName("indexed_lanes")] IReadOnlyList<string> IndexedLanes,
    [property: JsonPropertyName("last_index_run")] SemanticIndexRunSummary? LastIndexRun,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record SemanticIndexResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("run")] SemanticIndexRunSummary Run,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record SemanticHit(
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("semantic_score")] double SemanticScore,
    [property: JsonPropertyName("target_lane")] string TargetLane,
    [property: JsonPropertyName("target_kind")] string TargetKind,
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("document_id")] string? DocumentId,
    [property: JsonPropertyName("chunk_id")] string? ChunkId,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("message_id")] long? MessageId,
    [property: JsonPropertyName("snippet")] string Snippet,
    [property: JsonPropertyName("text_hash")] string TextHash,
    [property: JsonPropertyName("embedding_provider")] string EmbeddingProvider,
    [property: JsonPropertyName("embedding_model")] string EmbeddingModel,
    [property: JsonPropertyName("explanation")] string Explanation);

public sealed record SemanticQueryResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("semantic_enabled")] bool SemanticEnabled,
    [property: JsonPropertyName("hits")] IReadOnlyList<SemanticHit> Hits,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record HybridHit(
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("combined_score")] double CombinedScore,
    [property: JsonPropertyName("lexical_score")] double? LexicalScore,
    [property: JsonPropertyName("semantic_score")] double? SemanticScore,
    [property: JsonPropertyName("matched_terms")] IReadOnlyList<string> MatchedTerms,
    [property: JsonPropertyName("target_lane")] string TargetLane,
    [property: JsonPropertyName("target_kind")] string TargetKind,
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("document_id")] string? DocumentId,
    [property: JsonPropertyName("chunk_id")] string? ChunkId,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("explanation")] string Explanation);

public sealed record HybridSearchResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("semantic_participated")] bool SemanticParticipated,
    [property: JsonPropertyName("hits")] IReadOnlyList<HybridHit> Hits,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);
