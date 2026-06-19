using System.Text.Json.Serialization;

namespace Tiro.Cli;

public sealed record TiroSessionNoteIngestRequest(
    string DatabasePath,
    string SessionId,
    string Text,
    string SourceIdentity,
    string Direction = "operator",
    DateTimeOffset? TimestampUtc = null);

public sealed record TiroOperationalRecordIngestRequest(
    string DatabasePath,
    string RecordType,
    string Text,
    string SourceIdentity,
    string? SessionId = null,
    DateTimeOffset? TimestampUtc = null);

public sealed record TiroAichatSessionIngestRequest(
    string DatabasePath,
    string? SessionId,
    string? FilePath,
    string SourceIdentity,
    DateTimeOffset? TimestampUtc = null,
    int MaxChars = 50000,
    bool Latest = false,
    string? SessionsDirectory = null,
    bool Force = false);

public sealed record TiroIngestStateResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("db_path")] string? DatabasePath,
    [property: JsonPropertyName("selected_file")] string? SelectedFile,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("records_written")] int RecordsWritten,
    [property: JsonPropertyName("messages_written")] int MessagesWritten,
    [property: JsonPropertyName("operational_records_written")] int OperationalRecordsWritten,
    [property: JsonPropertyName("facts_written")] int FactsWritten,
    [property: JsonPropertyName("chunks_written")] int ChunksWritten,
    [property: JsonPropertyName("source_identity")] string SourceIdentity,
    [property: JsonPropertyName("timestamp_utc")] DateTimeOffset TimestampUtc,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors,
    [property: JsonPropertyName("written_items")] IReadOnlyList<TiroIngestWrittenItem> WrittenItems);

public sealed record TiroIngestWrittenItem(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("record_type")] string? RecordType,
    [property: JsonPropertyName("direction")] string? Direction,
    [property: JsonPropertyName("source_identity")] string SourceIdentity,
    [property: JsonPropertyName("timestamp_utc")] DateTimeOffset TimestampUtc,
    [property: JsonPropertyName("text_preview")] string TextPreview);

public sealed class TiroIngestStateService
{
    private static readonly string[] AllowedDirections = { "user", "assistant", "operator", "system" };
    private static readonly string[] AllowedRecordTypes = { "decision", "todo", "warning", "unknown" };

    public TiroIngestStateResponse IngestSessionNote(TiroSessionNoteIngestRequest request)
    {
        var timestamp = request.TimestampUtc ?? DateTimeOffset.UtcNow;
        var sessionId = RequireValue(request.SessionId, "session_id");
        var text = RequireValue(request.Text, "text");
        var sourceIdentity = RequireValue(request.SourceIdentity, "source_identity");
        var direction = NormalizeAllowed(request.Direction, AllowedDirections, "direction");

        using var store = TiroStore.Open(RequireValue(request.DatabasePath, "db_path"));
        var report = store.IngestMessage(sessionId, direction, sourceIdentity, text, timestamp);
        return Ok(
            "session_note",
            store.DatabasePath,
            null,
            sessionId,
            sourceIdentity,
            timestamp,
            messagesWritten: 1,
            operationalRecordsWritten: 0,
            warnings: Array.Empty<string>(),
            new[]
            {
                new TiroIngestWrittenItem(
                    "session_message",
                    report.MessageId,
                    sessionId,
                    null,
                    report.Direction,
                    report.SourceIdentity,
                    report.TimestampUtc,
                    Preview(text))
            });
    }

    public TiroIngestStateResponse IngestOperationalRecord(TiroOperationalRecordIngestRequest request)
    {
        var timestamp = request.TimestampUtc ?? DateTimeOffset.UtcNow;
        var recordType = NormalizeAllowed(request.RecordType, AllowedRecordTypes, "record_type");
        var text = RequireValue(request.Text, "text");
        var sourceIdentity = RequireValue(request.SourceIdentity, "source_identity");
        var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? null : request.SessionId.Trim();

        using var store = TiroStore.Open(RequireValue(request.DatabasePath, "db_path"));
        var report = store.AddOperationalRecord(recordType, text, sourceIdentity, sessionId, createdUtc: timestamp);
        return Ok(
            "operational_record",
            store.DatabasePath,
            null,
            report.SessionId,
            sourceIdentity,
            timestamp,
            messagesWritten: 0,
            operationalRecordsWritten: 1,
            warnings: sessionId is null
                ? new[] { "Operational record stored without session scope because no session_id was supplied." }
                : Array.Empty<string>(),
            new[]
            {
                new TiroIngestWrittenItem(
                    "operational_record",
                    report.RecordId,
                    report.SessionId,
                    report.RecordType,
                    null,
                    report.Origin,
                    report.CreatedUtc,
                    Preview(text))
            });
    }

    public TiroIngestStateResponse IngestAichatSession(TiroAichatSessionIngestRequest request)
    {
        var timestamp = request.TimestampUtc ?? DateTimeOffset.UtcNow;
        var sourceIdentity = RequireValue(request.SourceIdentity, "source_identity");
        if (request.MaxChars <= 0)
        {
            throw new ArgumentException("max_chars must be a positive integer.", nameof(request.MaxChars));
        }

        var selected = AichatSessionDiscovery.SelectSessionFile(request.FilePath, request.SessionsDirectory);
        var fullPath = selected.File;
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Saved AIChat session file not found: {fullPath}", fullPath);
        }

        var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? AichatSessionDiscovery.DeriveSessionId(fullPath)
            : request.SessionId.Trim();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Derived session_id is empty. Supply --session-id explicitly.", nameof(request.SessionId));
        }

        var content = File.ReadAllText(fullPath);
        var warnings = new List<string>
        {
            "Saved AIChat session imported as session evidence only; no facts or operational records were created."
        };
        warnings.AddRange(selected.Warnings);
        if (content.Length > request.MaxChars)
        {
            content = content[..request.MaxChars];
            warnings.Add($"Saved AIChat session content truncated to max_chars={request.MaxChars}.");
        }

        var parsed = AichatSessionParser.Parse(fullPath, content, timestamp);
        warnings.AddRange(parsed.Warnings);
        var databasePath = RequireValue(request.DatabasePath, "db_path");
        using var store = TiroStore.Open(databasePath);
        var writtenItems = new List<TiroIngestWrittenItem>();
        foreach (var message in parsed.Messages)
        {
            var report = store.IngestMessage(sessionId, message.Direction, sourceIdentity, message.Text, timestamp);
            writtenItems.Add(new TiroIngestWrittenItem(
                "session_message",
                report.MessageId,
                sessionId,
                null,
                report.Direction,
                report.SourceIdentity,
                report.TimestampUtc,
                Preview(message.Text)));
        }

        return Ok(
            "aichat_saved_session",
            store.DatabasePath,
            fullPath,
            sessionId,
            sourceIdentity,
            timestamp,
            messagesWritten: writtenItems.Count,
            operationalRecordsWritten: 0,
            warnings,
            writtenItems);
    }

    private static TiroIngestStateResponse Ok(
        string mode,
        string? databasePath,
        string? selectedFile,
        string? sessionId,
        string sourceIdentity,
        DateTimeOffset timestampUtc,
        int messagesWritten,
        int operationalRecordsWritten,
        IReadOnlyList<string> warnings,
        IReadOnlyList<TiroIngestWrittenItem> writtenItems)
    {
        return new TiroIngestStateResponse(
            "ok",
            mode,
            databasePath,
            selectedFile,
            sessionId,
            messagesWritten + operationalRecordsWritten,
            messagesWritten,
            operationalRecordsWritten,
            0,
            0,
            sourceIdentity,
            timestampUtc,
            warnings,
            Array.Empty<string>(),
            writtenItems);
    }

    private static string RequireValue(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Trim();
    }

    private static string NormalizeAllowed(string value, IReadOnlyList<string> allowed, string name)
    {
        var normalized = RequireValue(value, name).ToLowerInvariant();
        if (!allowed.Contains(normalized, StringComparer.Ordinal))
        {
            throw new ArgumentException($"{name} must be one of: {string.Join(", ", allowed)}.", name);
        }

        return normalized;
    }

    private static string Preview(string text)
    {
        var compact = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 160 ? compact : compact[..160];
    }
}
