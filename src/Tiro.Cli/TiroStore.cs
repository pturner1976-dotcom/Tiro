using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Tiro.Cli;

public sealed class TiroStore : IDisposable
{
    public static class FactStatus
    {
        public const string Active = "active";
        public const string Stale = "stale";
        public const string Superseded = "superseded";
        public const string Conflicting = "conflicting";
        public const string Unknown = "unknown";

        public static readonly string[] AllStatuses = new[] { Active, Stale, Superseded, Conflicting, Unknown };

        public static bool IsValid(string status) => Array.IndexOf(AllStatuses, status) >= 0;
    }

    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteDone = 101;
    private static readonly IntPtr SqliteTransient = new(-1);
    private readonly IntPtr _database;

    private TiroStore(string databasePath, IntPtr database)
    {
        DatabasePath = databasePath;
        _database = database;
    }

    public string DatabasePath { get; }

    public static TiroStore Open(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Database path must include a directory.");
        Directory.CreateDirectory(directory);

        var result = sqlite3_open(fullPath, out var database);
        if (result != SqliteOk)
        {
            var message = database == IntPtr.Zero ? "SQLite database could not be opened." : GetErrorMessage(database);
            if (database != IntPtr.Zero)
            {
                sqlite3_close_v2(database);
            }
            throw new InvalidOperationException(message);
        }

        var store = new TiroStore(fullPath, database);
        store.ExecuteNonQuery("PRAGMA foreign_keys = ON;");
        store.ExecuteNonQuery("PRAGMA journal_mode = WAL;");
        return store;
    }

    public void InitializeSchema()
    {
        ExecuteNonQuery(
            """
            CREATE TABLE IF NOT EXISTS schema_info (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            INSERT OR IGNORE INTO schema_info (key, value) VALUES ('schema_version', '1');

            CREATE TABLE IF NOT EXISTS sources (
                source_id TEXT PRIMARY KEY,
                source_name TEXT NOT NULL,
                source_path TEXT NOT NULL,
                status TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                last_ingested_utc TEXT
            );

            CREATE TABLE IF NOT EXISTS documents (
                document_id TEXT PRIMARY KEY,
                source_id TEXT NOT NULL,
                source_name TEXT NOT NULL,
                source_path TEXT NOT NULL,
                timeframe_or_era TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                FOREIGN KEY(source_id) REFERENCES sources(source_id)
            );

            CREATE TABLE IF NOT EXISTS chunks (
                chunk_id TEXT PRIMARY KEY,
                document_id TEXT NOT NULL,
                source_id TEXT NOT NULL,
                source_name TEXT NOT NULL,
                source_path TEXT NOT NULL,
                timeframe_or_era TEXT NOT NULL,
                chunk_index INTEGER NOT NULL,
                chunk_count INTEGER NOT NULL,
                text TEXT NOT NULL,
                ingested_utc TEXT NOT NULL,
                FOREIGN KEY(document_id) REFERENCES documents(document_id),
                FOREIGN KEY(source_id) REFERENCES sources(source_id)
            );

            CREATE INDEX IF NOT EXISTS idx_chunks_document_order
                ON chunks(document_id, chunk_index, chunk_id);

            CREATE INDEX IF NOT EXISTS idx_chunks_source
                ON chunks(source_id);

            CREATE TABLE IF NOT EXISTS sessions (
                session_id TEXT PRIMARY KEY,
                created_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS messages (
                message_id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                timestamp_utc TEXT NOT NULL,
                direction TEXT NOT NULL,
                source_id TEXT,
                source_identity TEXT NOT NULL DEFAULT '',
                text TEXT NOT NULL,
                FOREIGN KEY(session_id) REFERENCES sessions(session_id),
                FOREIGN KEY(source_id) REFERENCES sources(source_id)
            );

            CREATE INDEX IF NOT EXISTS idx_messages_session_time
                ON messages(session_id, timestamp_utc, message_id);

            CREATE TABLE IF NOT EXISTS operational_records (
                record_id INTEGER PRIMARY KEY AUTOINCREMENT,
                record_type TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                text TEXT NOT NULL,
                origin TEXT NOT NULL,
                session_id TEXT,
                status TEXT NOT NULL,
                linked_source_ids TEXT NOT NULL DEFAULT '',
                linked_message_ids TEXT NOT NULL DEFAULT '',
                FOREIGN KEY(session_id) REFERENCES sessions(session_id)
            );

            CREATE INDEX IF NOT EXISTS idx_operational_records_type_session
                ON operational_records(record_type, session_id, created_utc, record_id);

            CREATE TABLE IF NOT EXISTS facts (
                fact_id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_utc TEXT NOT NULL,
                text TEXT NOT NULL,
                status TEXT NOT NULL,
                source_id TEXT NOT NULL,
                origin_identity TEXT NOT NULL DEFAULT '',
                session_id TEXT,
                linked_source_ids TEXT NOT NULL DEFAULT '',
                linked_message_ids TEXT NOT NULL DEFAULT '',
                linked_record_ids TEXT NOT NULL DEFAULT '',
                supersedes_fact_id INTEGER,
                superseded_by_fact_id INTEGER,
                freshness_hint TEXT,
                expires_utc TEXT,
                FOREIGN KEY(source_id) REFERENCES sources(source_id),
                FOREIGN KEY(session_id) REFERENCES sessions(session_id),
                FOREIGN KEY(supersedes_fact_id) REFERENCES facts(fact_id),
                FOREIGN KEY(superseded_by_fact_id) REFERENCES facts(fact_id)
            );
            CREATE INDEX IF NOT EXISTS idx_facts_status ON facts(status);
            CREATE INDEX IF NOT EXISTS idx_facts_source ON facts(source_id);

            CREATE TABLE IF NOT EXISTS fact_conflicts (
                fact_id_1 INTEGER NOT NULL,
                fact_id_2 INTEGER NOT NULL,
                PRIMARY KEY (fact_id_1, fact_id_2),
                FOREIGN KEY(fact_id_1) REFERENCES facts(fact_id),
                FOREIGN KEY(fact_id_2) REFERENCES facts(fact_id)
            );

            CREATE TABLE IF NOT EXISTS evidence_pointers (
                pointer_id TEXT PRIMARY KEY,
                target_lane TEXT NOT NULL,
                target_kind TEXT NOT NULL,
                source_id TEXT,
                document_id TEXT,
                chunk_id_start TEXT,
                chunk_id_end TEXT,
                session_id TEXT,
                message_id_start INTEGER,
                message_id_end INTEGER,
                record_id INTEGER,
                fact_id INTEGER,
                text_hash TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                metadata_json TEXT NOT NULL DEFAULT '{}'
            );

            CREATE INDEX IF NOT EXISTS idx_evidence_pointers_source_id ON evidence_pointers(source_id);
            CREATE INDEX IF NOT EXISTS idx_evidence_pointers_document_id ON evidence_pointers(document_id);
            CREATE INDEX IF NOT EXISTS idx_evidence_pointers_session_id ON evidence_pointers(session_id);

            CREATE TABLE IF NOT EXISTS recall_proxies (
                proxy_id TEXT PRIMARY KEY,
                lane TEXT NOT NULL,
                proxy_type TEXT NOT NULL,
                title TEXT NOT NULL,
                breadcrumb TEXT NOT NULL,
                summary TEXT NOT NULL,
                keywords TEXT NOT NULL DEFAULT '',
                entities TEXT NOT NULL DEFAULT '',
                source_id TEXT,
                document_id TEXT,
                session_id TEXT,
                record_id INTEGER,
                fact_id INTEGER,
                pointer_id TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'active',
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                metadata_json TEXT NOT NULL DEFAULT '{}',
                FOREIGN KEY(pointer_id) REFERENCES evidence_pointers(pointer_id)
            );

            CREATE INDEX IF NOT EXISTS idx_recall_proxies_lane ON recall_proxies(lane);
            CREATE INDEX IF NOT EXISTS idx_recall_proxies_document_id ON recall_proxies(document_id);
            CREATE INDEX IF NOT EXISTS idx_recall_proxies_source_id ON recall_proxies(source_id);
            CREATE INDEX IF NOT EXISTS idx_recall_proxies_status ON recall_proxies(status);
            CREATE INDEX IF NOT EXISTS idx_recall_proxies_pointer_id ON recall_proxies(pointer_id);
            """);
        EnsureColumn("messages", "source_identity", "TEXT NOT NULL DEFAULT ''");
    }

    public OperationalRecordReport AddOperationalRecord(
        string recordType,
        string text,
        string origin,
        string? sessionId = null,
        string status = "open",
        IReadOnlyList<string>? linkedSourceIds = null,
        IReadOnlyList<long>? linkedMessageIds = null,
        DateTimeOffset? createdUtc = null)
    {
        InitializeSchema();
        var normalizedType = NormalizeOperationalRecordType(recordType);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Operational record text is required.", nameof(text));
        }
        if (string.IsNullOrWhiteSpace(origin))
        {
            throw new ArgumentException("Operational record origin is required.", nameof(origin));
        }
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            CreateSession(sessionId, createdUtc);
        }

        var timestamp = createdUtc ?? DateTimeOffset.UtcNow;
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "open" : status.Trim().ToLowerInvariant();
        using var statement = Prepare(
            """
            INSERT INTO operational_records (
                record_type, created_utc, text, origin, session_id, status, linked_source_ids, linked_message_ids)
            VALUES (
                $record_type, $created_utc, $text, $origin, NULLIF($session_id, ''), $status, $linked_source_ids, $linked_message_ids);
            """);
        statement.BindText(1, normalizedType);
        statement.BindText(2, FormatTimestamp(timestamp));
        statement.BindText(3, text.Trim());
        statement.BindText(4, origin.Trim());
        statement.BindText(5, sessionId ?? string.Empty);
        statement.BindText(6, normalizedStatus);
        statement.BindText(7, SerializeStringList(linkedSourceIds));
        statement.BindText(8, SerializeLongList(linkedMessageIds));
        statement.StepDone();

        return new OperationalRecordReport(
            sqlite3_last_insert_rowid(_database),
            normalizedType,
            timestamp,
            origin.Trim(),
            string.IsNullOrWhiteSpace(sessionId) ? null : sessionId,
            normalizedStatus);
    }

    public IReadOnlyList<OperationalRecord> ListOperationalRecords(string? recordType, string? sessionId, int limit)
    {
        InitializeSchema();
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        }

        var normalizedType = string.IsNullOrWhiteSpace(recordType) ? string.Empty : NormalizeOperationalRecordType(recordType);
        using var statement = Prepare(
            """
            SELECT record_id, record_type, created_utc, text, origin, session_id, status, linked_source_ids, linked_message_ids
            FROM operational_records
            WHERE ($record_type = '' OR record_type = $record_type)
              AND ($session_id = '' OR session_id = $session_id)
            ORDER BY created_utc DESC, record_id DESC
            LIMIT $limit;
            """);
        statement.BindText(1, normalizedType);
        statement.BindText(2, sessionId ?? string.Empty);
        statement.BindInt(3, limit);
        var records = new List<OperationalRecord>();
        while (statement.StepRow())
        {
            records.Add(ReadOperationalRecord(statement));
        }

        return records;
    }

    public IReadOnlyList<TiroSessionInventoryItem> ListSessionInventory(int limit)
    {
        InitializeSchema();
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        }

        using var statement = Prepare(
            """
            SELECT s.session_id,
                   COUNT(m.message_id) AS message_count,
                   MIN(m.timestamp_utc) AS first_timestamp_utc,
                   MAX(m.timestamp_utc) AS last_timestamp_utc,
                   GROUP_CONCAT(DISTINCT m.source_identity) AS source_identities
            FROM sessions s
            LEFT JOIN messages m ON m.session_id = s.session_id
            GROUP BY s.session_id
            ORDER BY COALESCE(MAX(m.timestamp_utc), s.created_utc) DESC, s.session_id ASC
            LIMIT $limit;
            """);
        statement.BindInt(1, limit);
        var sessions = new List<TiroSessionInventoryItem>();
        while (statement.StepRow())
        {
            sessions.Add(new TiroSessionInventoryItem(
                statement.ColumnText(0),
                statement.ColumnInt(1),
                ParseOptionalTimestamp(statement.ColumnTextOrNull(2)),
                ParseOptionalTimestamp(statement.ColumnTextOrNull(3)),
                SplitCommaList(statement.ColumnTextOrNull(4))));
        }

        return sessions;
    }

    public IReadOnlyList<TiroSourceInventoryItem> ListSourceInventory(int limit)
    {
        InitializeSchema();
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        }

        using var statement = Prepare(
            """
            SELECT s.source_id, s.source_name, s.source_path, s.status, s.created_utc, s.last_ingested_utc,
                   COUNT(DISTINCT d.document_id) AS document_count,
                   COUNT(c.chunk_id) AS chunk_count
            FROM sources s
            LEFT JOIN documents d ON d.source_id = s.source_id
            LEFT JOIN chunks c ON c.source_id = s.source_id
            GROUP BY s.source_id, s.source_name, s.source_path, s.status, s.created_utc, s.last_ingested_utc
            ORDER BY COALESCE(s.last_ingested_utc, s.created_utc) DESC, s.source_id ASC
            LIMIT $limit;
            """);
        statement.BindInt(1, limit);
        var sources = new List<TiroSourceInventoryItem>();
        while (statement.StepRow())
        {
            sources.Add(new TiroSourceInventoryItem(
                statement.ColumnText(0),
                statement.ColumnText(1),
                statement.ColumnText(2),
                statement.ColumnText(3),
                ParseTimestamp(statement.ColumnText(4)),
                ParseOptionalTimestamp(statement.ColumnTextOrNull(5)),
                statement.ColumnInt(6),
                statement.ColumnInt(7)));
        }

        return sources;
    }

    public IReadOnlyList<TiroDocumentInventoryItem> ListDocumentInventory(string? sourceId, int limit)
    {
        InitializeSchema();
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        }

        using var statement = Prepare(
            """
            SELECT d.document_id, d.source_id, d.source_name, d.source_path, d.timeframe_or_era, d.created_utc,
                   COUNT(c.chunk_id) AS chunk_count,
                   MIN(c.chunk_id) AS first_chunk_id,
                   MAX(c.chunk_id) AS last_chunk_id
            FROM documents d
            LEFT JOIN chunks c ON c.document_id = d.document_id
            WHERE ($source_id = '' OR d.source_id = $source_id)
            GROUP BY d.document_id, d.source_id, d.source_name, d.source_path, d.timeframe_or_era, d.created_utc
            ORDER BY d.created_utc DESC, d.document_id ASC
            LIMIT $limit;
            """);
        statement.BindText(1, sourceId ?? string.Empty);
        statement.BindInt(2, limit);
        var documents = new List<TiroDocumentInventoryItem>();
        while (statement.StepRow())
        {
            documents.Add(new TiroDocumentInventoryItem(
                statement.ColumnText(0),
                statement.ColumnText(1),
                statement.ColumnText(2),
                statement.ColumnText(3),
                statement.ColumnText(4),
                ParseTimestamp(statement.ColumnText(5)),
                statement.ColumnInt(6),
                statement.ColumnTextOrNull(7),
                statement.ColumnTextOrNull(8)));
        }

        return documents;
    }

    public IReadOnlyList<TiroDocumentInventoryItem> ListDocumentsForProxyBuild(string? sourceId = null, string? documentId = null)
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            SELECT d.document_id, d.source_id, d.source_name, d.source_path, d.timeframe_or_era, d.created_utc,
                   COUNT(c.chunk_id) AS chunk_count,
                   MIN(c.chunk_id) AS first_chunk_id,
                   MAX(c.chunk_id) AS last_chunk_id
            FROM documents d
            LEFT JOIN chunks c ON c.document_id = d.document_id
            WHERE ($source_id = '' OR d.source_id = $source_id)
              AND ($document_id = '' OR d.document_id = $document_id)
            GROUP BY d.document_id, d.source_id, d.source_name, d.source_path, d.timeframe_or_era, d.created_utc
            ORDER BY d.created_utc ASC, d.document_id ASC;
            """);
        statement.BindText(1, sourceId ?? string.Empty);
        statement.BindText(2, documentId ?? string.Empty);
        var results = new List<TiroDocumentInventoryItem>();
        while (statement.StepRow())
        {
            results.Add(new TiroDocumentInventoryItem(
                statement.ColumnText(0),
                statement.ColumnText(1),
                statement.ColumnText(2),
                statement.ColumnText(3),
                statement.ColumnText(4),
                ParseTimestamp(statement.ColumnText(5)),
                statement.ColumnInt(6),
                statement.ColumnTextOrNull(7),
                statement.ColumnTextOrNull(8)));
        }

        return results;
    }

    public IReadOnlyList<Chunk> ListChunksByDocument(string documentId)
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            SELECT chunk_id, document_id, source_id, source_name, source_path, timeframe_or_era,
                   chunk_index, chunk_count, text, ingested_utc
            FROM chunks
            WHERE document_id = $document_id
            ORDER BY chunk_index ASC, chunk_id ASC;
            """);
        statement.BindText(1, documentId);
        var chunks = new List<Chunk>();
        while (statement.StepRow())
        {
            chunks.Add(new Chunk(
                statement.ColumnText(0),
                statement.ColumnText(1),
                statement.ColumnText(2),
                statement.ColumnText(3),
                statement.ColumnText(4),
                statement.ColumnText(5),
                statement.ColumnInt(6),
                statement.ColumnInt(7),
                statement.ColumnText(8),
                ParseTimestamp(statement.ColumnText(9))));
        }

        return chunks;
    }

    public Chunk? GetChunk(string chunkId)
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            SELECT chunk_id, document_id, source_id, source_name, source_path, timeframe_or_era,
                   chunk_index, chunk_count, text, ingested_utc
            FROM chunks
            WHERE chunk_id = $chunk_id;
            """);
        statement.BindText(1, chunkId);
        return statement.StepRow()
            ? new Chunk(
                statement.ColumnText(0),
                statement.ColumnText(1),
                statement.ColumnText(2),
                statement.ColumnText(3),
                statement.ColumnText(4),
                statement.ColumnText(5),
                statement.ColumnInt(6),
                statement.ColumnInt(7),
                statement.ColumnText(8),
                ParseTimestamp(statement.ColumnText(9)))
            : null;
    }

    public IReadOnlyList<Chunk> GetChunkRange(string documentId, string chunkIdStart, string? chunkIdEnd)
    {
        InitializeSchema();
        var startChunk = GetChunk(chunkIdStart);
        if (startChunk is null || !string.Equals(startChunk.DocumentId, documentId, StringComparison.Ordinal))
        {
            return Array.Empty<Chunk>();
        }

        var endChunk = string.IsNullOrWhiteSpace(chunkIdEnd) ? startChunk : GetChunk(chunkIdEnd);
        if (endChunk is null || !string.Equals(endChunk.DocumentId, documentId, StringComparison.Ordinal))
        {
            return Array.Empty<Chunk>();
        }

        var startIndex = Math.Min(startChunk.ChunkIndex, endChunk.ChunkIndex);
        var endIndex = Math.Max(startChunk.ChunkIndex, endChunk.ChunkIndex);
        using var statement = Prepare(
            """
            SELECT chunk_id, document_id, source_id, source_name, source_path, timeframe_or_era,
                   chunk_index, chunk_count, text, ingested_utc
            FROM chunks
            WHERE document_id = $document_id
              AND chunk_index BETWEEN $start_index AND $end_index
            ORDER BY chunk_index ASC, chunk_id ASC;
            """);
        statement.BindText(1, documentId);
        statement.BindInt(2, startIndex);
        statement.BindInt(3, endIndex);
        var chunks = new List<Chunk>();
        while (statement.StepRow())
        {
            chunks.Add(new Chunk(
                statement.ColumnText(0),
                statement.ColumnText(1),
                statement.ColumnText(2),
                statement.ColumnText(3),
                statement.ColumnText(4),
                statement.ColumnText(5),
                statement.ColumnInt(6),
                statement.ColumnInt(7),
                statement.ColumnText(8),
                ParseTimestamp(statement.ColumnText(9))));
        }

        return chunks;
    }

    public int CountSessionMessages(string sessionId)
    {
        InitializeSchema();
        using var statement = Prepare("SELECT COUNT(*) FROM messages WHERE session_id = $session_id;");
        statement.BindText(1, sessionId);
        return statement.StepRow() ? statement.ColumnInt(0) : 0;
    }

    public IReadOnlyList<Message> GetSessionMessagesChronological(string sessionId, int limit)
    {
        InitializeSchema();
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        }

        using var statement = Prepare(
            """
            SELECT message_id, session_id, timestamp_utc, direction, source_id, source_identity, text
            FROM messages
            WHERE session_id = $session_id
            ORDER BY timestamp_utc ASC, message_id ASC
            LIMIT $limit;
            """);
        statement.BindText(1, sessionId);
        statement.BindInt(2, limit);
        var messages = new List<Message>();
        while (statement.StepRow())
        {
            messages.Add(ReadMessage(statement));
        }

        return messages;
    }

    public IReadOnlyList<Message> GetRecentMessagesAcrossSessions(int limit)
    {
        InitializeSchema();
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        }

        using var statement = Prepare(
            """
            SELECT message_id, session_id, timestamp_utc, direction, source_id, source_identity, text
            FROM messages
            ORDER BY timestamp_utc DESC, message_id DESC
            LIMIT $limit;
            """);
        statement.BindInt(1, limit);
        var messages = new List<Message>();
        while (statement.StepRow())
        {
            messages.Add(ReadMessage(statement));
        }

        return messages;
    }

    public IReadOnlyList<TiroPhraseSearchResult> PhraseSearch(string phrase, string lane, string? sessionId, int limit)
    {
        InitializeSchema();
        if (string.IsNullOrWhiteSpace(phrase))
        {
            throw new ArgumentException("Phrase is required.", nameof(phrase));
        }
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        }

        var normalizedLane = string.IsNullOrWhiteSpace(lane) ? "all" : lane.Trim().ToLowerInvariant();
        if (normalizedLane is not ("session" or "operational" or "corpus" or "facts" or "all"))
        {
            throw new ArgumentException("Lane must be one of: session, operational, corpus, facts, all.", nameof(lane));
        }

        var results = new List<TiroPhraseSearchResult>();
        if (normalizedLane is "session" or "all")
        {
            AddSessionPhraseResults(phrase, sessionId, limit, results);
        }
        if (results.Count < limit && normalizedLane is "operational" or "all")
        {
            AddOperationalPhraseResults(phrase, normalizedLane == "operational" ? sessionId : null, limit, results);
        }
        if (results.Count < limit && normalizedLane is "corpus" or "all")
        {
            AddCorpusPhraseResults(phrase, limit, results);
        }
        if (results.Count < limit && normalizedLane is "facts" or "all")
        {
            AddFactPhraseResults(phrase, normalizedLane == "facts" ? sessionId : null, limit, results);
        }

        return results.Take(limit).ToArray();
    }

    public IReadOnlyList<OperationalMemoryEvidence> QueryOperationalRecords(string query, int limit, string? sessionId = null)
    {
        InitializeSchema();
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<OperationalMemoryEvidence>();
        }
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        }

        var terms = LexicalSearch.Tokenize(query);
        if (terms.Count == 0)
        {
            return Array.Empty<OperationalMemoryEvidence>();
        }

        using var statement = Prepare(
            """
            SELECT record_id, record_type, created_utc, text, origin, session_id, status, linked_source_ids, linked_message_ids
            FROM operational_records
            WHERE ($session_id = '' OR session_id = $session_id)
            ORDER BY created_utc ASC, record_id ASC;
            """);
        statement.BindText(1, sessionId ?? string.Empty);

        var evidence = new List<OperationalMemoryEvidence>();
        while (statement.StepRow())
        {
            var record = ReadOperationalRecord(statement);
            var score = LexicalSearch.Score(
                record.Text,
                $"{record.RecordType} {record.Origin} {record.SessionId ?? string.Empty} {record.Status} {string.Join(' ', record.LinkedSourceIds)} {string.Join(' ', record.LinkedMessageIds)}",
                terms);
            if (score.Score <= 0)
            {
                continue;
            }

            evidence.Add(new OperationalMemoryEvidence(
                0,
                record.RecordId,
                record.RecordType,
                record.CreatedUtc,
                record.Text,
                record.Origin,
                record.SessionId,
                record.Status,
                record.LinkedSourceIds,
                record.LinkedMessageIds,
                score.Score,
                score.MatchedTerms,
                score.Details,
                score.Summary,
                BuildExplanation(score, terms),
                LexicalSearch.BuildSnippet(record.Text, terms)));
        }

        return evidence
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.CreatedUtc)
            .ThenByDescending(result => result.RecordId)
            .Take(limit)
            .Select((result, index) => result with { Rank = index + 1 })
            .ToArray();
    }

    public SessionCreateReport CreateSession(string sessionId, DateTimeOffset? createdUtc = null)
    {
        InitializeSchema();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session ID is required.", nameof(sessionId));
        }

        var timestamp = createdUtc ?? DateTimeOffset.UtcNow;
        using var statement = Prepare(
            """
            INSERT OR IGNORE INTO sessions (session_id, created_utc)
            VALUES ($session_id, $created_utc);
            """);
        statement.BindText(1, sessionId);
        statement.BindText(2, FormatTimestamp(timestamp));
        statement.StepDone();
        var created = sqlite3_changes(_database) > 0;
        var stored = GetSession(sessionId) ?? new Session(sessionId, timestamp);
        return new SessionCreateReport(stored.SessionId, created, stored.CreatedUtc);
    }

    public Session? GetSession(string sessionId)
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            SELECT session_id, created_utc
            FROM sessions
            WHERE session_id = $session_id;
            """);
        statement.BindText(1, sessionId);
        return statement.StepRow()
            ? new Session(statement.ColumnText(0), ParseTimestamp(statement.ColumnText(1)))
            : null;
    }

    public MessageIngestReport IngestMessage(
        string sessionId,
        string direction,
        string sourceIdentity,
        string text,
        DateTimeOffset? timestampUtc = null)
    {
        InitializeSchema();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session ID is required.", nameof(sessionId));
        }
        if (string.IsNullOrWhiteSpace(direction))
        {
            throw new ArgumentException("Message direction is required.", nameof(direction));
        }
        if (string.IsNullOrWhiteSpace(sourceIdentity))
        {
            throw new ArgumentException("Message source identity is required.", nameof(sourceIdentity));
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Message text is required.", nameof(text));
        }

        CreateSession(sessionId, timestampUtc);
        var timestamp = timestampUtc ?? DateTimeOffset.UtcNow;
        using var statement = Prepare(
            """
            INSERT INTO messages (session_id, timestamp_utc, direction, source_id, source_identity, text)
            VALUES ($session_id, $timestamp_utc, $direction, NULL, $source_identity, $text);
            """);
        statement.BindText(1, sessionId);
        statement.BindText(2, FormatTimestamp(timestamp));
        statement.BindText(3, NormalizeDirection(direction));
        statement.BindText(4, sourceIdentity.Trim());
        statement.BindText(5, text.Trim());
        statement.StepDone();
        return new MessageIngestReport(sqlite3_last_insert_rowid(_database), sessionId, timestamp, NormalizeDirection(direction), sourceIdentity.Trim());
    }

    public IReadOnlyList<Message> GetRecentMessages(string sessionId, int limit)
    {
        InitializeSchema();
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        }

        using var statement = Prepare(
            """
            SELECT message_id, session_id, timestamp_utc, direction, source_id, source_identity, text
            FROM (
                SELECT message_id, session_id, timestamp_utc, direction, source_id, source_identity, text
                FROM messages
                WHERE session_id = $session_id
                ORDER BY timestamp_utc DESC, message_id DESC
                LIMIT $limit
            )
            ORDER BY timestamp_utc ASC, message_id ASC;
            """);
        statement.BindText(1, sessionId);
        statement.BindInt(2, limit);
        var messages = new List<Message>();
        while (statement.StepRow())
        {
            messages.Add(ReadMessage(statement));
        }

        return messages;
    }

    public IReadOnlyList<SessionMessageEvidence> QuerySessionMessages(string sessionId, string query, int limit)
    {
        InitializeSchema();
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(sessionId))
        {
            return Array.Empty<SessionMessageEvidence>();
        }
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        }

        var terms = LexicalSearch.Tokenize(query);
        if (terms.Count == 0)
        {
            return Array.Empty<SessionMessageEvidence>();
        }

        using var statement = Prepare(
            """
            SELECT message_id, session_id, timestamp_utc, direction, source_id, source_identity, text
            FROM messages
            WHERE session_id = $session_id
            ORDER BY timestamp_utc ASC, message_id ASC;
            """);
        statement.BindText(1, sessionId);

        var results = new List<SessionMessageEvidence>();
        while (statement.StepRow())
        {
            var message = ReadMessage(statement);
            var score = LexicalSearch.Score(
                message.Text,
                $"{message.SessionId} {message.Direction} {message.SourceIdentity} message:{message.MessageId}",
                terms);
            if (score.Score <= 0)
            {
                continue;
            }

            results.Add(new SessionMessageEvidence(
                0,
                message.MessageId,
                message.SessionId,
                message.TimestampUtc,
                message.Direction,
                message.SourceIdentity,
                score.Score,
                score.MatchedTerms,
                score.Details,
                score.Summary,
                BuildExplanation(score, terms),
                LexicalSearch.BuildSnippet(message.Text, terms)));
        }

        return results
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.TimestampUtc)
            .ThenByDescending(result => result.MessageId)
            .Take(limit)
            .Select((result, index) => result with { Rank = index + 1 })
            .ToArray();
    }

    public IngestionReport IngestChunks(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        }

        var fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Chunk JSONL input file not found: {fullPath}", fullPath);
        }

        InitializeSchema();
        var ingestedUtc = DateTimeOffset.UtcNow;
        var lineNumber = 0;
        var insertedChunks = 0;
        var duplicateChunks = 0;
        var sourcesRegistered = 0;
        var documentsRegistered = 0;
        var seenInputChunkIds = new HashSet<string>(StringComparer.Ordinal);

        ExecuteNonQuery("BEGIN TRANSACTION;");
        try
        {
            foreach (var line in File.ReadLines(fullPath))
            {
                lineNumber++;
                var record = ChunkJsonRecord.Parse(line, lineNumber);
                if (!seenInputChunkIds.Add(record.ChunkId!))
                {
                    throw new InvalidOperationException($"JSONL line {lineNumber} duplicates chunk_id '{record.ChunkId}' inside the input file.");
                }

                var sourceId = BuildSourceId(record.SourceName!, record.SourcePath!);
                if (RegisterSource(sourceId, record.SourceName!, record.SourcePath!, ingestedUtc))
                {
                    sourcesRegistered++;
                }

                if (RegisterDocument(record.DocumentId!, sourceId, record.SourceName!, record.SourcePath!, record.TimeframeOrEra!, ingestedUtc))
                {
                    documentsRegistered++;
                }

                if (InsertChunk(record, sourceId, ingestedUtc))
                {
                    insertedChunks++;
                }
                else
                {
                    duplicateChunks++;
                }
            }

            ExecuteNonQuery("COMMIT;");
        }
        catch
        {
            ExecuteNonQuery("ROLLBACK;");
            throw;
        }

        return new IngestionReport(fullPath, lineNumber, insertedChunks, duplicateChunks, sourcesRegistered, documentsRegistered, ingestedUtc);
    }

    public IReadOnlyList<RetrievalResult> Query(string query, int limit, RetrievalFilters? filters = null)
    {
        // Existing implementation unchanged
        InitializeSchema();
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<RetrievalResult>();
        }
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        }

        var terms = LexicalSearch.Tokenize(query);
        if (terms.Count == 0)
        {
            return Array.Empty<RetrievalResult>();
        }
        filters ??= new RetrievalFilters(null, null);

        using var statement = Prepare(
            """
            SELECT chunk_id, document_id, source_id, source_name, source_path, timeframe_or_era,
                   chunk_index, chunk_count, text, ingested_utc
            FROM chunks
            WHERE ($source_id = '' OR source_id = $source_id)
              AND ($document_id = '' OR document_id = $document_id)
            ORDER BY document_id ASC, chunk_index ASC, chunk_id ASC;
            """);
        statement.BindText(1, filters.SourceId ?? string.Empty);
        statement.BindText(2, filters.DocumentId ?? string.Empty);

        var results = new List<RetrievalResult>();
        while (statement.StepRow())
        {
            var text = statement.ColumnText(8);
            var lexicalScore = LexicalSearch.Score(
                text,
                $"{statement.ColumnText(1)} {statement.ColumnText(2)} {statement.ColumnText(3)} {statement.ColumnText(5)} {statement.ColumnText(0)}",
                terms);
            if (lexicalScore.Score <= 0)
            {
                continue;
            }

            results.Add(new RetrievalResult(
                0,
                statement.ColumnText(0),
                statement.ColumnText(1),
                statement.ColumnText(2),
                statement.ColumnText(3),
                statement.ColumnText(4),
                statement.ColumnText(5),
                statement.ColumnInt(6),
                statement.ColumnInt(7),
                ParseTimestamp(statement.ColumnText(9)),
                lexicalScore.Score,
                lexicalScore.MatchedTerms,
                lexicalScore.Details,
                lexicalScore.Summary,
                BuildExplanation(lexicalScore, terms),
                LexicalSearch.BuildSnippet(text, terms)));
        }

        var ranked = results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.DocumentId, StringComparer.Ordinal)
            .ThenBy(result => result.ChunkIndex)
            .ThenBy(result => result.ChunkId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
        return ranked
            .Select((result, index) => result with { Rank = index + 1 })
            .ToArray();
    }

    /// <summary>
    /// Query lifecycle-aware facts matching all terms in the query (lexical).
    /// </summary>
    public IReadOnlyList<FactRecord> QueryLifecycleFacts(string query, int limit, string? sessionId = null)
    {
        InitializeSchema();
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<FactRecord>();
        }
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        }

        var terms = LexicalSearch.Tokenize(query);
        if (terms.Count == 0)
        {
            return Array.Empty<FactRecord>();
        }

        using var statement = Prepare(
            """
            SELECT fact_id, created_utc, text, status, source_id, origin_identity, session_id,
                linked_source_ids, linked_message_ids, linked_record_ids, supersedes_fact_id, superseded_by_fact_id, freshness_hint, expires_utc
            FROM facts
            WHERE ($session_id = '' OR session_id = $session_id)
            ORDER BY created_utc DESC, fact_id DESC;
            """);
        statement.BindText(1, sessionId ?? string.Empty);

        var facts = new List<FactRecord>();
        while (statement.StepRow())
        {
            var text = statement.ColumnText(2);
            // Score textual match against all terms
            var score = LexicalSearch.Score(text, text, terms);
            if (score.Score <= 0)
            {
                continue;
            }

            int? supersedesFactId = statement.ColumnInt(10);
            if (supersedesFactId == 0) supersedesFactId = null;
            int? supersededByFactId = statement.ColumnInt(11);
            if (supersededByFactId == 0) supersededByFactId = null;

            facts.Add(new FactRecord(
                statement.ColumnInt(0),
                ParseTimestamp(statement.ColumnText(1)),
                text,
                statement.ColumnText(3), // status
                statement.ColumnText(4),
                statement.ColumnText(5),
                statement.ColumnTextOrNull(6),
                DeserializeStringList(statement.ColumnText(7)),
                DeserializeLongList(statement.ColumnText(8)),
                DeserializeLongList(statement.ColumnText(9)),
                supersedesFactId,
                supersededByFactId,
                string.IsNullOrWhiteSpace(statement.ColumnText(12)) ? null : statement.ColumnText(12),
                string.IsNullOrWhiteSpace(statement.ColumnText(13)) ? null : ParseTimestamp(statement.ColumnText(13))
                ));
        }

        return facts
            .OrderByDescending(fact => fact.CreatedUtc)
                .ThenByDescending(fact => fact.FactId)
            .Take(limit)
            .ToArray();
    }

    public bool HasChunksForFilters(RetrievalFilters filters)
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            SELECT 1
            FROM chunks
            WHERE ($source_id = '' OR source_id = $source_id)
              AND ($document_id = '' OR document_id = $document_id)
            LIMIT 1;
            """);
        statement.BindText(1, filters.SourceId ?? string.Empty);
        statement.BindText(2, filters.DocumentId ?? string.Empty);
        return statement.StepRow();
    }

    public IReadOnlyList<SupportingContext> GetSupportingContext(IReadOnlyList<RetrievalResult> primaryResults, int contextWindow)
    {
        InitializeSchema();
        if (contextWindow <= 0 || primaryResults.Count == 0)
        {
            return Array.Empty<SupportingContext>();
        }

        contextWindow = Math.Min(contextWindow, 2);
        var primaryChunkIds = primaryResults.Select(result => result.ChunkId).ToHashSet(StringComparer.Ordinal);
        var seenContextChunkIds = new HashSet<string>(StringComparer.Ordinal);
        var context = new List<SupportingContext>();
        foreach (var primary in primaryResults.OrderBy(result => result.Rank))
        {
            var start = Math.Max(1, primary.ChunkIndex - contextWindow);
            var end = Math.Min(primary.ChunkCount, primary.ChunkIndex + contextWindow);
            using var statement = Prepare(
                """
                SELECT chunk_id, document_id, source_id, source_name, source_path, timeframe_or_era,
                       chunk_index, chunk_count, text
                FROM chunks
                WHERE document_id = $document_id
                  AND chunk_index BETWEEN $start AND $end
                ORDER BY chunk_index ASC, chunk_id ASC;
                """);
            statement.BindText(1, primary.DocumentId);
            statement.BindInt(2, start);
            statement.BindInt(3, end);

            while (statement.StepRow())
            {
                var chunkId = statement.ColumnText(0);
                if (primaryChunkIds.Contains(chunkId) || !seenContextChunkIds.Add($"{primary.ChunkId}|{chunkId}"))
                {
                    continue;
                }

                var chunkIndex = statement.ColumnInt(6);
                var distance = Math.Abs(chunkIndex - primary.ChunkIndex);
                context.Add(new SupportingContext(
                    primary.ChunkId,
                    chunkId,
                    statement.ColumnText(1),
                    statement.ColumnText(2),
                    statement.ColumnText(3),
                    statement.ColumnText(4),
                    statement.ColumnText(5),
                    chunkIndex,
                    statement.ColumnInt(7),
                    chunkIndex < primary.ChunkIndex ? "previous" : "next",
                    distance,
                    LexicalSearch.BuildSnippet(statement.ColumnText(8), primary.MatchedTerms)));
            }
        }

        return context
            .OrderBy(item => primaryResults.First(result => result.ChunkId == item.PrimaryChunkId).Rank)
            .ThenBy(item => item.Distance)
            .ThenBy(item => item.ChunkIndex)
            .ThenBy(item => item.ChunkId, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<Source> GetSourcesByIds(IEnumerable<string> sourceIds)
    {
        InitializeSchema();
        var unique = sourceIds.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var sources = new List<Source>();
        foreach (var sourceId in unique)
        {
            using var statement = Prepare(
                """
                SELECT source_id, source_name, source_path, status, created_utc, last_ingested_utc
                FROM sources
                WHERE source_id = $source_id;
                """);
            statement.BindText(1, sourceId);
            if (statement.StepRow())
            {
                sources.Add(ReadSource(statement));
            }
        }
        return sources;
    }

    public int CountRecallProxies(string? lane = null, string? status = null)
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            SELECT COUNT(*)
            FROM recall_proxies
            WHERE ($lane = '' OR lane = $lane)
              AND ($status = '' OR status = $status);
            """);
        statement.BindText(1, lane ?? string.Empty);
        statement.BindText(2, status ?? string.Empty);
        return statement.StepRow() ? statement.ColumnInt(0) : 0;
    }

    public int CountActiveRecallProxiesForSelection(string? sourceId, string? documentId, string lane = "corpus")
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            SELECT COUNT(*)
            FROM recall_proxies
            WHERE lane = $lane
              AND status = 'active'
              AND ($source_id = '' OR source_id = $source_id)
              AND ($document_id = '' OR document_id = $document_id);
            """);
        statement.BindText(1, lane);
        statement.BindText(2, sourceId ?? string.Empty);
        statement.BindText(3, documentId ?? string.Empty);
        return statement.StepRow() ? statement.ColumnInt(0) : 0;
    }

    public int SupersedeActiveRecallProxies(string? sourceId, string? documentId, string lane = "corpus")
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            UPDATE recall_proxies
            SET status = 'superseded',
                updated_utc = $updated_utc
            WHERE lane = $lane
              AND status = 'active'
              AND ($source_id = '' OR source_id = $source_id)
              AND ($document_id = '' OR document_id = $document_id);
            """);
        statement.BindText(1, FormatTimestamp(DateTimeOffset.UtcNow));
        statement.BindText(2, lane);
        statement.BindText(3, sourceId ?? string.Empty);
        statement.BindText(4, documentId ?? string.Empty);
        statement.StepDone();
        return sqlite3_changes(_database);
    }

    public void InsertEvidencePointer(TiroEvidencePointer pointer)
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            INSERT INTO evidence_pointers (
                pointer_id, target_lane, target_kind, source_id, document_id, chunk_id_start, chunk_id_end,
                session_id, message_id_start, message_id_end, record_id, fact_id, text_hash, created_utc, metadata_json)
            VALUES (
                $pointer_id, $target_lane, $target_kind, NULLIF($source_id, ''), NULLIF($document_id, ''), NULLIF($chunk_id_start, ''), NULLIF($chunk_id_end, ''),
                NULLIF($session_id, ''), $message_id_start, $message_id_end, $record_id, $fact_id, $text_hash, $created_utc, $metadata_json);
            """);
        statement.BindText(1, pointer.PointerId);
        statement.BindText(2, pointer.TargetLane);
        statement.BindText(3, pointer.TargetKind);
        statement.BindText(4, pointer.SourceId ?? string.Empty);
        statement.BindText(5, pointer.DocumentId ?? string.Empty);
        statement.BindText(6, pointer.ChunkIdStart ?? string.Empty);
        statement.BindText(7, pointer.ChunkIdEnd ?? string.Empty);
        statement.BindText(8, pointer.SessionId ?? string.Empty);
        BindNullableLong(statement, 9, pointer.MessageIdStart);
        BindNullableLong(statement, 10, pointer.MessageIdEnd);
        BindNullableLong(statement, 11, pointer.RecordId);
        BindNullableInt(statement, 12, pointer.FactId);
        statement.BindText(13, pointer.TextHash);
        statement.BindText(14, FormatTimestamp(pointer.CreatedUtc));
        statement.BindText(15, pointer.MetadataJson);
        statement.StepDone();
    }

    public void InsertRecallProxy(TiroRecallProxy proxy)
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            INSERT INTO recall_proxies (
                proxy_id, lane, proxy_type, title, breadcrumb, summary, keywords, entities, source_id, document_id,
                session_id, record_id, fact_id, pointer_id, status, created_utc, updated_utc, metadata_json)
            VALUES (
                $proxy_id, $lane, $proxy_type, $title, $breadcrumb, $summary, $keywords, $entities, NULLIF($source_id, ''), NULLIF($document_id, ''),
                NULLIF($session_id, ''), $record_id, $fact_id, $pointer_id, $status, $created_utc, $updated_utc, $metadata_json);
            """);
        statement.BindText(1, proxy.ProxyId);
        statement.BindText(2, proxy.Lane);
        statement.BindText(3, proxy.ProxyType);
        statement.BindText(4, proxy.Title);
        statement.BindText(5, proxy.Breadcrumb);
        statement.BindText(6, proxy.Summary);
        statement.BindText(7, proxy.Keywords);
        statement.BindText(8, proxy.Entities);
        statement.BindText(9, proxy.SourceId ?? string.Empty);
        statement.BindText(10, proxy.DocumentId ?? string.Empty);
        statement.BindText(11, proxy.SessionId ?? string.Empty);
        BindNullableLong(statement, 12, proxy.RecordId);
        BindNullableInt(statement, 13, proxy.FactId);
        statement.BindText(14, proxy.PointerId);
        statement.BindText(15, proxy.Status);
        statement.BindText(16, FormatTimestamp(proxy.CreatedUtc));
        statement.BindText(17, FormatTimestamp(proxy.UpdatedUtc));
        statement.BindText(18, proxy.MetadataJson);
        statement.StepDone();
    }

    public IReadOnlyList<TiroRecallProxy> ListRecallProxies(string? lane, string? documentId, string? sourceId, string? status, int limit)
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            SELECT proxy_id, lane, proxy_type, title, breadcrumb, summary, keywords, entities, source_id, document_id,
                   session_id, record_id, fact_id, pointer_id, status, created_utc, updated_utc, metadata_json
            FROM recall_proxies
            WHERE ($lane = '' OR lane = $lane)
              AND ($document_id = '' OR document_id = $document_id)
              AND ($source_id = '' OR source_id = $source_id)
              AND ($status = '' OR status = $status)
            ORDER BY
                CASE status
                    WHEN 'active' THEN 0
                    WHEN 'stale' THEN 1
                    WHEN 'superseded' THEN 2
                    WHEN 'hidden' THEN 3
                    ELSE 4
                END,
                updated_utc DESC,
                proxy_id ASC
            LIMIT $limit;
            """);
        statement.BindText(1, lane ?? string.Empty);
        statement.BindText(2, documentId ?? string.Empty);
        statement.BindText(3, sourceId ?? string.Empty);
        statement.BindText(4, status ?? string.Empty);
        statement.BindInt(5, limit);
        var proxies = new List<TiroRecallProxy>();
        while (statement.StepRow())
        {
            proxies.Add(ReadRecallProxy(statement));
        }

        return proxies;
    }

    public IReadOnlyList<TiroRecallProxy> ListRecallProxiesForSearch()
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            SELECT proxy_id, lane, proxy_type, title, breadcrumb, summary, keywords, entities, source_id, document_id,
                   session_id, record_id, fact_id, pointer_id, status, created_utc, updated_utc, metadata_json
            FROM recall_proxies
            WHERE status <> 'hidden'
            ORDER BY updated_utc DESC, proxy_id ASC;
            """);
        var proxies = new List<TiroRecallProxy>();
        while (statement.StepRow())
        {
            proxies.Add(ReadRecallProxy(statement));
        }

        return proxies;
    }

    public TiroEvidencePointer? GetEvidencePointer(string pointerId)
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            SELECT pointer_id, target_lane, target_kind, source_id, document_id, chunk_id_start, chunk_id_end,
                   session_id, message_id_start, message_id_end, record_id, fact_id, text_hash, created_utc, metadata_json
            FROM evidence_pointers
            WHERE pointer_id = $pointer_id;
            """);
        statement.BindText(1, pointerId);
        return statement.StepRow() ? ReadEvidencePointer(statement) : null;
    }

    public StoreStats GetStats()
    {
        InitializeSchema();
        return new StoreStats(
            CountRows("sources"),
            CountRows("documents"),
            CountRows("chunks"),
            CountRows("sessions"),
            CountRows("messages"),
            CountRows("operational_records"),
            CountRows("facts"),
            CountRows("fact_conflicts"),
            SumTextLength("chunks", "text"));
    }

    public string GetSchemaVersion()
    {
        InitializeSchema();
        using var statement = Prepare("SELECT value FROM schema_info WHERE key = 'schema_version' LIMIT 1;");
        return statement.StepRow() ? statement.ColumnText(0) : "unknown";
    }

    public IReadOnlyList<string> ListTableNames()
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
            ORDER BY name ASC;
            """);
        var names = new List<string>();
        while (statement.StepRow())
        {
            names.Add(statement.ColumnText(0));
        }

        return names;
    }

    public IReadOnlyList<string> ListIndexNames()
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'index' AND name NOT LIKE 'sqlite_%'
            ORDER BY name ASC;
            """);
        var names = new List<string>();
        while (statement.StepRow())
        {
            names.Add(statement.ColumnText(0));
        }

        return names;
    }

    public TiroInitRecordCounts GetInitRecordCounts()
    {
        InitializeSchema();
        return new TiroInitRecordCounts(
            CountRows("sources") + CountRows("documents") + CountRows("chunks"),
            CountRows("sessions") + CountRows("messages"),
            CountRows("operational_records"),
            CountRows("facts") + CountRows("fact_conflicts"),
            CountRows("recall_proxies"),
            CountRows("evidence_pointers"));
    }

    public void Dispose()
    {
        sqlite3_close_v2(_database);
    }

    // --- Fact lifecycle management ---

    public FactRecord AddFact(
        string text,
        string sourceId,
        string originIdentity,
        string status = FactStatus.Active,
        string? sessionId = null,
        IReadOnlyList<string>? linkedSourceIds = null,
        IReadOnlyList<long>? linkedMessageIds = null,
        IReadOnlyList<long>? linkedRecordIds = null,
        int? supersedesFactId = null,
        string? freshnessHint = null,
        DateTimeOffset? expiresUtc = null,
        DateTimeOffset? createdUtc = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Fact text is required.", nameof(text));
        }
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException("Fact source_id is required.", nameof(sourceId));
        }
        if (!FactStatus.IsValid(status))
        {
            throw new ArgumentException($"Invalid fact status '{status}'.", nameof(status));
        }

        InitializeSchema();
        var now = createdUtc ?? DateTimeOffset.UtcNow;
        EnsureFactSource(sourceId.Trim(), now);
        if (!string.IsNullOrEmpty(sessionId))
        {
            CreateSession(sessionId);
        }

        using var statement = Prepare(
            """
            INSERT INTO facts (
                created_utc, text, status, source_id, origin_identity, session_id,
                linked_source_ids, linked_message_ids, linked_record_ids, supersedes_fact_id, freshness_hint, expires_utc)
            VALUES (
                $created_utc, $text, $status, $source_id, $origin_identity, NULLIF($session_id, ''),
                $linked_source_ids, $linked_message_ids, $linked_record_ids, $supersedes_fact_id, $freshness_hint, $expires_utc);
            """);

        statement.BindText(1, FormatTimestamp(now));
        statement.BindText(2, text.Trim());
        statement.BindText(3, status);
        statement.BindText(4, sourceId.Trim());
        statement.BindText(5, originIdentity.Trim());
        statement.BindText(6, sessionId ?? string.Empty);
        statement.BindText(7, SerializeStringList(linkedSourceIds));
        statement.BindText(8, SerializeLongList(linkedMessageIds));
        statement.BindText(9, SerializeLongList(linkedRecordIds));
        if (supersedesFactId.HasValue)
        {
            statement.BindInt(10, supersedesFactId.Value);
        }
        else
        {
            statement.BindNull(10);
        }
        statement.BindText(11, freshnessHint ?? string.Empty);
        statement.BindText(12, expiresUtc.HasValue ? FormatTimestamp(expiresUtc.Value) : string.Empty);
        statement.StepDone();

        var factId = (int)sqlite3_last_insert_rowid(_database);
        return new FactRecord(
            factId,
            now,
            text.Trim(),
            status,
            sourceId.Trim(),
            originIdentity.Trim(),
            sessionId,
            linkedSourceIds ?? Array.Empty<string>(),
            linkedMessageIds ?? Array.Empty<long>(),
            linkedRecordIds ?? Array.Empty<long>(),
            supersedesFactId,
            null,
            freshnessHint,
            expiresUtc);
    }

    public bool UpdateFactStatus(int factId, string newStatus)
    {
        InitializeSchema();
        if (!FactStatus.IsValid(newStatus))
        {
            throw new ArgumentException($"Invalid fact status '{newStatus}'.", nameof(newStatus));
        }

        using var statement = Prepare(
            """
            UPDATE facts
            SET status = $status
            WHERE fact_id = $fact_id;
            """);
        statement.BindText(1, newStatus);
        statement.BindInt(2, factId);
        statement.StepDone();

        return sqlite3_changes(_database) > 0;
    }

    public bool SupersedeFact(int supersedingFactId, int supersededFactId)
    {
        InitializeSchema();
        if (supersedingFactId == supersededFactId)
        {
            throw new ArgumentException("A fact cannot supersede itself.", nameof(supersededFactId));
        }

        using var statement = Prepare(
            """
            UPDATE facts
            SET supersedes_fact_id = $supersedes_fact_id
            WHERE fact_id = $fact_id;
            """);
        statement.BindInt(1, supersededFactId);
        statement.BindInt(2, supersedingFactId);
        statement.StepDone();
        var firstChanges = sqlite3_changes(_database);

        using var statement2 = Prepare(
            """
            UPDATE facts
            SET superseded_by_fact_id = $superseded_by_fact_id,
                status = $status
            WHERE fact_id = $fact_id;
            """);
        statement2.BindInt(1, supersedingFactId);
        statement2.BindText(2, FactStatus.Superseded);
        statement2.BindInt(3, supersededFactId);
        statement2.StepDone();
        var secondChanges = sqlite3_changes(_database);

        return firstChanges + secondChanges > 0;
    }

    public void AddFactConflict(int factId1, int factId2)
    {
        InitializeSchema();
        if (factId1 == factId2)
        {
            throw new ArgumentException("A fact cannot conflict with itself.", nameof(factId2));
        }

        using var statement = Prepare(
            """
            INSERT OR IGNORE INTO fact_conflicts (fact_id_1, fact_id_2) VALUES ($fact_id_1, $fact_id_2);
            """);
        statement.BindInt(1, Math.Min(factId1, factId2));
        statement.BindInt(2, Math.Max(factId1, factId2));
        statement.StepDone();

        using var statusStatement = Prepare(
            """
            UPDATE facts
            SET status = $status
            WHERE fact_id IN ($fact_id_1, $fact_id_2);
            """);
        statusStatement.BindText(1, FactStatus.Conflicting);
        statusStatement.BindInt(2, factId1);
        statusStatement.BindInt(3, factId2);
        statusStatement.StepDone();
    }

    public IReadOnlyList<FactConflictRecord> ListFactConflicts(int? factId = null)
    {
        InitializeSchema();
        using var statement = Prepare(
            """
            SELECT fact_id_1, fact_id_2
            FROM fact_conflicts
            WHERE ($fact_id = 0 OR fact_id_1 = $fact_id OR fact_id_2 = $fact_id)
            ORDER BY fact_id_1 ASC, fact_id_2 ASC;
            """);
        statement.BindInt(1, factId ?? 0);

        var results = new List<FactConflictRecord>();
        while (statement.StepRow())
        {
            results.Add(new FactConflictRecord(statement.ColumnInt(0), statement.ColumnInt(1)));
        }

        return results;
    }

    public IReadOnlyList<FactRecord> ListFacts(string? statusFilter = null, int limit = 100)
    {
        InitializeSchema();

        string sql = "SELECT fact_id, created_utc, text, status, source_id, origin_identity, session_id, linked_source_ids, linked_message_ids, linked_record_ids, supersedes_fact_id, superseded_by_fact_id, freshness_hint, expires_utc FROM facts";
        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            sql += " WHERE status = $status";
        }
        sql += " ORDER BY created_utc DESC, fact_id DESC LIMIT $limit";

        using var statement = Prepare(sql);
        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            statement.BindText(1, statusFilter);
            statement.BindInt(2, limit);
        }
        else
        {
            statement.BindInt(1, limit);
        }

        var results = new List<FactRecord>();
        while (statement.StepRow())
        {
            int? supersedesFactId = statement.ColumnInt(10);
            if (supersedesFactId == 0) supersedesFactId = null;
            int? supersededByFactId = statement.ColumnInt(11);
            if (supersededByFactId == 0) supersededByFactId = null;

            results.Add(new FactRecord(
                statement.ColumnInt(0),
                ParseTimestamp(statement.ColumnText(1)),
                statement.ColumnText(2),
                statement.ColumnText(3),
                statement.ColumnText(4),
                statement.ColumnText(5),
                statement.ColumnTextOrNull(6),
                DeserializeStringList(statement.ColumnText(7)),
                DeserializeLongList(statement.ColumnText(8)),
                DeserializeLongList(statement.ColumnText(9)),
                supersedesFactId,
                supersededByFactId,
                string.IsNullOrWhiteSpace(statement.ColumnText(12)) ? null : statement.ColumnText(12),
                string.IsNullOrWhiteSpace(statement.ColumnText(13)) ? null : ParseTimestamp(statement.ColumnText(13))
                ));
        }

        return results;
    }



    private bool RegisterSource(string sourceId, string sourceName, string sourcePath, DateTimeOffset ingestedUtc)
    {
        var existed = SourceExists(sourceId);
        using var statement = Prepare(
            """
            INSERT INTO sources (source_id, source_name, source_path, status, created_utc, last_ingested_utc)
            VALUES ($source_id, $source_name, $source_path, 'active', $created_utc, $last_ingested_utc)
            ON CONFLICT(source_id) DO UPDATE SET
                source_name = excluded.source_name,
                source_path = excluded.source_path,
                last_ingested_utc = excluded.last_ingested_utc;
            """);
        statement.BindText(1, sourceId);
        statement.BindText(2, sourceName);
        statement.BindText(3, sourcePath);
        statement.BindText(4, FormatTimestamp(ingestedUtc));
        statement.BindText(5, FormatTimestamp(ingestedUtc));
        statement.StepDone();
        return !existed;
    }

    private bool SourceExists(string sourceId)
    {
        using var statement = Prepare("SELECT 1 FROM sources WHERE source_id = $source_id;");
        statement.BindText(1, sourceId);
        return statement.StepRow();
    }

    private void EnsureFactSource(string sourceId, DateTimeOffset createdUtc)
    {
        if (SourceExists(sourceId))
        {
            return;
        }

        using var statement = Prepare(
            """
            INSERT INTO sources (source_id, source_name, source_path, status, created_utc, last_ingested_utc)
            VALUES ($source_id, $source_name, $source_path, 'active', $created_utc, NULL);
            """);
        statement.BindText(1, sourceId);
        statement.BindText(2, sourceId);
        statement.BindText(3, $"fact-lifecycle:{sourceId}");
        statement.BindText(4, FormatTimestamp(createdUtc));
        statement.StepDone();
    }

    private bool RegisterDocument(string documentId, string sourceId, string sourceName, string sourcePath, string timeframeOrEra, DateTimeOffset createdUtc)
    {
        using var statement = Prepare(
            """
            INSERT OR IGNORE INTO documents (document_id, source_id, source_name, source_path, timeframe_or_era, created_utc)
            VALUES ($document_id, $source_id, $source_name, $source_path, $timeframe_or_era, $created_utc);
            """);
        statement.BindText(1, documentId);
        statement.BindText(2, sourceId);
        statement.BindText(3, sourceName);
        statement.BindText(4, sourcePath);
        statement.BindText(5, timeframeOrEra);
        statement.BindText(6, FormatTimestamp(createdUtc));
        statement.StepDone();
        return sqlite3_changes(_database) > 0;
    }

    private bool InsertChunk(ChunkJsonRecord record, string sourceId, DateTimeOffset ingestedUtc)
    {
        using var statement = Prepare(
            """
            INSERT OR IGNORE INTO chunks (
                chunk_id, document_id, source_id, source_name, source_path, timeframe_or_era,
                chunk_index, chunk_count, text, ingested_utc)
            VALUES ($chunk_id, $document_id, $source_id, $source_name, $source_path, $timeframe_or_era,
                $chunk_index, $chunk_count, $text, $ingested_utc);
            """);
        statement.BindText(1, record.ChunkId!);
        statement.BindText(2, record.DocumentId!);
        statement.BindText(3, sourceId);
        statement.BindText(4, record.SourceName!);
        statement.BindText(5, record.SourcePath!);
        statement.BindText(6, record.TimeframeOrEra!);
        statement.BindInt(7, record.ChunkIndex);
        statement.BindInt(8, record.ChunkCount);
        statement.BindText(9, record.Text!);
        statement.BindText(10, FormatTimestamp(ingestedUtc));
        statement.StepDone();
        return sqlite3_changes(_database) > 0;
    }

    private static string BuildExplanation(LexicalScore score, IReadOnlyList<string> terms)
    {
        var coverage = terms.Count == 0 ? 0 : score.MatchedTerms.Count * 100 / terms.Count;
        return $"Matched {score.MatchedTerms.Count}/{terms.Count} normalized terms ({coverage}% coverage); term_score={score.Summary.TermScore}, coverage_bonus={score.Summary.CoverageBonus}, phrase_bonus={score.Summary.PhraseBonus}, density_bonus={score.Summary.DensityBonus}.";
    }

    private static void BindNullableLong(SqliteStatement statement, int index, long? value)
    {
        if (value.HasValue)
        {
            statement.BindInt64(index, value.Value);
        }
        else
        {
            statement.BindNull(index);
        }
    }

    private static void BindNullableInt(SqliteStatement statement, int index, int? value)
    {
        if (value.HasValue)
        {
            statement.BindInt(index, value.Value);
        }
        else
        {
            statement.BindNull(index);
        }
    }

    private int CountRows(string tableName)
    {
        using var statement = Prepare($"SELECT COUNT(*) FROM {tableName};");
        return statement.StepRow() ? statement.ColumnInt(0) : 0;
    }

    private int SumTextLength(string tableName, string columnName)
    {
        using var statement = Prepare($"SELECT COALESCE(SUM(LENGTH({columnName})), 0) FROM {tableName};");
        return statement.StepRow() ? statement.ColumnInt(0) : 0;
    }

    private static Source ReadSource(SqliteStatement statement)
    {
        var lastIngested = statement.ColumnTextOrNull(5);
        return new Source(
            statement.ColumnText(0),
            statement.ColumnText(1),
            statement.ColumnText(2),
            statement.ColumnText(3),
            ParseTimestamp(statement.ColumnText(4)),
            lastIngested is null ? null : ParseTimestamp(lastIngested));
    }

    private static TiroRecallProxy ReadRecallProxy(SqliteStatement statement)
    {
        return new TiroRecallProxy(
            statement.ColumnText(0),
            statement.ColumnText(1),
            statement.ColumnText(2),
            statement.ColumnText(3),
            statement.ColumnText(4),
            statement.ColumnText(5),
            statement.ColumnText(6),
            statement.ColumnText(7),
            statement.ColumnTextOrNull(8),
            statement.ColumnTextOrNull(9),
            statement.ColumnTextOrNull(10),
            statement.ColumnInt64OrNull(11),
            statement.ColumnInt(12) is var factId && factId != 0 ? factId : null,
            statement.ColumnText(13),
            statement.ColumnText(14),
            ParseTimestamp(statement.ColumnText(15)),
            ParseTimestamp(statement.ColumnText(16)),
            statement.ColumnText(17));
    }

    private static TiroEvidencePointer ReadEvidencePointer(SqliteStatement statement)
    {
        return new TiroEvidencePointer(
            statement.ColumnText(0),
            statement.ColumnText(1),
            statement.ColumnText(2),
            statement.ColumnTextOrNull(3),
            statement.ColumnTextOrNull(4),
            statement.ColumnTextOrNull(5),
            statement.ColumnTextOrNull(6),
            statement.ColumnTextOrNull(7),
            statement.ColumnInt64OrNull(8),
            statement.ColumnInt64OrNull(9),
            statement.ColumnInt64OrNull(10),
            statement.ColumnInt(11) is var factId && factId != 0 ? factId : null,
            statement.ColumnText(12),
            ParseTimestamp(statement.ColumnText(13)),
            statement.ColumnText(14));
    }

    private static Message ReadMessage(SqliteStatement statement)
    {
        return new Message(
            statement.ColumnLong(0),
            statement.ColumnText(1),
            ParseTimestamp(statement.ColumnText(2)),
            statement.ColumnText(3),
            statement.ColumnTextOrNull(4),
            statement.ColumnText(5),
            statement.ColumnText(6));
    }

    private static OperationalRecord ReadOperationalRecord(SqliteStatement statement)
    {
        var sessionId = statement.ColumnTextOrNull(5);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = null;
        }

        return new OperationalRecord(
            statement.ColumnLong(0),
            statement.ColumnText(1),
            ParseTimestamp(statement.ColumnText(2)),
            statement.ColumnText(3),
            statement.ColumnText(4),
            sessionId,
            statement.ColumnText(6),
            DeserializeStringList(statement.ColumnText(7)),
            DeserializeLongList(statement.ColumnText(8)));
    }

    private void EnsureColumn(string tableName, string columnName, string definition)
    {
        if (ColumnExists(tableName, columnName))
        {
            return;
        }

        ExecuteNonQuery($"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};");
    }

    private bool ColumnExists(string tableName, string columnName)
    {
        using var statement = Prepare($"PRAGMA table_info({tableName});");
        while (statement.StepRow())
        {
            if (string.Equals(statement.ColumnText(1), columnName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeDirection(string direction)
    {
        var normalized = direction.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Message direction is required.", nameof(direction));
        }

        return normalized;
    }

    private static string NormalizeOperationalRecordType(string recordType)
    {
        var normalized = recordType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "decision" or "todo" or "unknown" or "warning" => normalized,
            _ => throw new ArgumentException("Operational record type must be one of: decision, todo, unknown, warning.", nameof(recordType))
        };
    }

    private static string SerializeStringList(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return string.Empty;
        }

        return string.Join('\n', values.Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal));
    }

    private static string SerializeLongList(IReadOnlyList<long>? values)
    {
        if (values is null || values.Count == 0)
        {
            return string.Empty;
        }

        return string.Join('\n', values.Distinct().OrderBy(value => value));
    }

    private static IReadOnlyList<string> DeserializeStringList(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IReadOnlyList<long> DeserializeLongList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<long>();
        }

        return value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => long.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
            .Where(item => item > 0)
            .ToArray();
    }

    private void AddSessionPhraseResults(string phrase, string? sessionId, int limit, List<TiroPhraseSearchResult> results)
    {
        using var statement = Prepare(
            """
            SELECT message_id, session_id, timestamp_utc, source_identity, text
            FROM messages
            WHERE text LIKE $phrase ESCAPE '\'
              AND ($session_id = '' OR session_id = $session_id)
            ORDER BY timestamp_utc DESC, message_id DESC
            LIMIT $limit;
            """);
        statement.BindText(1, BuildLikePattern(phrase));
        statement.BindText(2, sessionId ?? string.Empty);
        statement.BindInt(3, Math.Max(1, limit - results.Count));
        while (statement.StepRow())
        {
            results.Add(new TiroPhraseSearchResult(
                "session",
                statement.ColumnLong(0).ToString(CultureInfo.InvariantCulture),
                statement.ColumnText(1),
                statement.ColumnText(3),
                ParseTimestamp(statement.ColumnText(2)),
                BuildPhraseSnippet(statement.ColumnText(4), phrase)));
        }
    }

    private void AddOperationalPhraseResults(string phrase, string? sessionId, int limit, List<TiroPhraseSearchResult> results)
    {
        using var statement = Prepare(
            """
            SELECT record_id, session_id, created_utc, origin, text
            FROM operational_records
            WHERE text LIKE $phrase ESCAPE '\'
              AND ($session_id = '' OR session_id = $session_id)
            ORDER BY created_utc DESC, record_id DESC
            LIMIT $limit;
            """);
        statement.BindText(1, BuildLikePattern(phrase));
        statement.BindText(2, sessionId ?? string.Empty);
        statement.BindInt(3, Math.Max(1, limit - results.Count));
        while (statement.StepRow())
        {
            results.Add(new TiroPhraseSearchResult(
                "operational",
                statement.ColumnLong(0).ToString(CultureInfo.InvariantCulture),
                statement.ColumnTextOrNull(1),
                statement.ColumnText(3),
                ParseTimestamp(statement.ColumnText(2)),
                BuildPhraseSnippet(statement.ColumnText(4), phrase)));
        }
    }

    private void AddCorpusPhraseResults(string phrase, int limit, List<TiroPhraseSearchResult> results)
    {
        using var statement = Prepare(
            """
            SELECT chunk_id, ingested_utc, source_name, text
            FROM chunks
            WHERE text LIKE $phrase ESCAPE '\'
            ORDER BY ingested_utc DESC, chunk_id ASC
            LIMIT $limit;
            """);
        statement.BindText(1, BuildLikePattern(phrase));
        statement.BindInt(2, Math.Max(1, limit - results.Count));
        while (statement.StepRow())
        {
            results.Add(new TiroPhraseSearchResult(
                "corpus",
                statement.ColumnText(0),
                null,
                statement.ColumnText(2),
                ParseTimestamp(statement.ColumnText(1)),
                BuildPhraseSnippet(statement.ColumnText(3), phrase)));
        }
    }

    private void AddFactPhraseResults(string phrase, string? sessionId, int limit, List<TiroPhraseSearchResult> results)
    {
        using var statement = Prepare(
            """
            SELECT fact_id, session_id, created_utc, origin_identity, text
            FROM facts
            WHERE text LIKE $phrase ESCAPE '\'
              AND ($session_id = '' OR session_id = $session_id)
            ORDER BY created_utc DESC, fact_id DESC
            LIMIT $limit;
            """);
        statement.BindText(1, BuildLikePattern(phrase));
        statement.BindText(2, sessionId ?? string.Empty);
        statement.BindInt(3, Math.Max(1, limit - results.Count));
        while (statement.StepRow())
        {
            results.Add(new TiroPhraseSearchResult(
                "facts",
                statement.ColumnInt(0).ToString(CultureInfo.InvariantCulture),
                statement.ColumnTextOrNull(1),
                statement.ColumnText(3),
                ParseTimestamp(statement.ColumnText(2)),
                BuildPhraseSnippet(statement.ColumnText(4), phrase)));
        }
    }

    private static string BuildLikePattern(string phrase)
    {
        var escaped = phrase.Trim()
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static string BuildPhraseSnippet(string text, string phrase)
    {
        var clean = Regex.Replace(text, @"\s+", " ").Trim();
        var index = clean.IndexOf(phrase.Trim(), StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return clean.Length <= 240 ? clean : clean[..240] + "...";
        }

        var start = Math.Max(0, index - 90);
        var end = Math.Min(clean.Length, index + phrase.Trim().Length + 120);
        var prefix = start > 0 ? "..." : string.Empty;
        var suffix = end < clean.Length ? "..." : string.Empty;
        return prefix + clean[start..end] + suffix;
    }

    private static IReadOnlyList<string> SplitCommaList(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
    }

    private static DateTimeOffset? ParseOptionalTimestamp(string? timestamp)
    {
        return string.IsNullOrWhiteSpace(timestamp) ? null : ParseTimestamp(timestamp);
    }

    private static string BuildSourceId(string sourceName, string sourcePath)
    {
        var raw = $"{sourceName}|{sourcePath}";
        var builder = new StringBuilder("source:");
        foreach (var character in raw.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder[^1] != '-')
            {
                builder.Append('-');
            }
        }
        return builder.ToString().TrimEnd('-');
    }

    private SqliteStatement Prepare(string sql)
    {
        var result = sqlite3_prepare_v2(_database, sql, -1, out var statement, IntPtr.Zero);
        if (result != SqliteOk)
        {
            throw new InvalidOperationException(GetErrorMessage(_database));
        }
        return new SqliteStatement(_database, statement);
    }

    private void ExecuteNonQuery(string sql)
    {
        var result = sqlite3_exec(_database, sql, IntPtr.Zero, IntPtr.Zero, out var errorPointer);
        if (result == SqliteOk)
        {
            return;
        }

        var message = errorPointer == IntPtr.Zero
            ? GetErrorMessage(_database)
            : Marshal.PtrToStringUTF8(errorPointer) ?? GetErrorMessage(_database);
        if (errorPointer != IntPtr.Zero)
        {
            sqlite3_free(errorPointer);
        }
        throw new InvalidOperationException(message);
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string timestamp) => DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static string GetErrorMessage(IntPtr database) => Marshal.PtrToStringUTF8(sqlite3_errmsg(database)) ?? "Unknown SQLite error.";

    private sealed class SqliteStatement : IDisposable
    {
        private readonly IntPtr _database;
        private readonly IntPtr _statement;
        private bool _hasCompleted;

        public SqliteStatement(IntPtr database, IntPtr statement)
        {
            _database = database;
            _statement = statement;
        }

        public void BindText(int index, string value)
        {
            var result = sqlite3_bind_text(_statement, index, value, -1, SqliteTransient);
            ThrowIfNotOk(result);
        }

        public void BindInt(int index, int value)
        {
            var result = sqlite3_bind_int(_statement, index, value);
            ThrowIfNotOk(result);
        }

        public void BindInt64(int index, long value)
        {
            var result = sqlite3_bind_int64(_statement, index, value);
            ThrowIfNotOk(result);
        }

        public void BindNull(int index)
        {
            var result = sqlite3_bind_null(_statement, index);
            ThrowIfNotOk(result);
        }

        public bool StepRow()
        {
            var result = sqlite3_step(_statement);
            if (result == SqliteRow)
            {
                return true;
            }
            if (result == SqliteDone)
            {
                _hasCompleted = true;
                return false;
            }
            throw new InvalidOperationException(GetErrorMessage(_database));
        }

        public void StepDone()
        {
            var result = sqlite3_step(_statement);
            if (result != SqliteDone)
            {
                throw new InvalidOperationException(GetErrorMessage(_database));
            }
            _hasCompleted = true;
        }

        public int ColumnInt(int index) => sqlite3_column_int(_statement, index);

        public long ColumnLong(int index) => sqlite3_column_int64(_statement, index);

        public long? ColumnInt64OrNull(int index)
        {
            return sqlite3_column_type(_statement, index) == SqliteNull ? null : sqlite3_column_int64(_statement, index);
        }

        public string ColumnText(int index) => Marshal.PtrToStringUTF8(sqlite3_column_text(_statement, index)) ?? string.Empty;

        public string? ColumnTextOrNull(int index)
        {
            var pointer = sqlite3_column_text(_statement, index);
            return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);
        }

        public void Dispose()
        {
            if (!_hasCompleted)
            {
                while (sqlite3_step(_statement) == SqliteRow)
                {
                }
            }
            sqlite3_finalize(_statement);
        }

        private void ThrowIfNotOk(int result)
        {
            if (result != SqliteOk)
            {
                throw new InvalidOperationException(GetErrorMessage(_database));
            }
        }
    }

    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open(string filename, out IntPtr database);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close_v2(IntPtr database);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr database);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr database, string sql, IntPtr callback, IntPtr firstArgument, out IntPtr errorMessage);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void sqlite3_free(IntPtr pointer);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr database, string sql, int byteCount, out IntPtr statement, IntPtr tail);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_text(IntPtr statement, int index, string value, int byteCount, IntPtr destructor);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_int(IntPtr statement, int index, int value);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_int(IntPtr statement, int index);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(IntPtr statement, int index);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_type(IntPtr statement, int index);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int index);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_changes(IntPtr database);
    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_last_insert_rowid(IntPtr database);

    [DllImport("libsqlite3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_null(IntPtr stmt, int index);

    private const int SqliteNull = 5;
}
