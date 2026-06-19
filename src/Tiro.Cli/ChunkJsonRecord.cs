using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tiro.Cli;

public sealed record ChunkJsonRecord
{
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("source_name")]
    public string? SourceName { get; init; }

    [JsonPropertyName("source_path")]
    public string? SourcePath { get; init; }

    [JsonPropertyName("timeframe_or_era")]
    public string? TimeframeOrEra { get; init; }

    [JsonPropertyName("chunk_id")]
    public string? ChunkId { get; init; }

    [JsonPropertyName("chunk_index")]
    public int ChunkIndex { get; init; }

    [JsonPropertyName("chunk_count")]
    public int ChunkCount { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    public static ChunkJsonRecord Parse(string line, int lineNumber)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new InvalidOperationException($"JSONL line {lineNumber} is empty.");
        }

        ChunkJsonRecord record;
        try
        {
            record = JsonSerializer.Deserialize<ChunkJsonRecord>(line, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("record is empty");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"JSONL line {lineNumber} is malformed JSON: {ex.Message}", ex);
        }

        record.Validate(lineNumber);
        return record;
    }

    public void Validate(int lineNumber)
    {
        RequireValue(DocumentId, "document_id", lineNumber);
        RequireValue(SourceName, "source_name", lineNumber);
        RequireValue(SourcePath, "source_path", lineNumber);
        RequireValue(TimeframeOrEra, "timeframe_or_era", lineNumber);
        RequireValue(ChunkId, "chunk_id", lineNumber);
        RequireValue(Text, "text", lineNumber);

        if (ChunkIndex < 1)
        {
            throw new InvalidOperationException($"JSONL line {lineNumber} has invalid chunk_index: {ChunkIndex}.");
        }

        if (ChunkCount < 1)
        {
            throw new InvalidOperationException($"JSONL line {lineNumber} has invalid chunk_count: {ChunkCount}.");
        }

        if (ChunkIndex > ChunkCount)
        {
            throw new InvalidOperationException($"JSONL line {lineNumber} has chunk_index greater than chunk_count: {ChunkIndex}/{ChunkCount}.");
        }
    }

    private static void RequireValue(string? value, string fieldName, int lineNumber)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"JSONL line {lineNumber} is missing required field '{fieldName}'.");
        }
    }
}
