using System.Security.Cryptography;
using System.Text;

namespace Tiro.Cli;

public sealed class TiroProxyService
{
    private const string CorpusLane = "corpus";

    public TiroProxyBuildResponse BuildCorpusProxies(TiroProxyBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var store = TiroStore.Open(request.DatabasePath);
        store.InitializeSchema();

        var warnings = new List<string>();
        var errors = new List<string>();
        var documents = store.ListDocumentsForProxyBuild(request.SourceId, request.DocumentId);
        if (documents.Count == 0)
        {
            warnings.Add("No corpus documents matched the requested selection.");
            return new TiroProxyBuildResponse("ok", store.DatabasePath, request.Mode, request.SourceId, request.DocumentId, 0, 0, 0, warnings, errors);
        }

        if (!request.Rebuild)
        {
            var skipped = new List<string>();
            foreach (var document in documents)
            {
                if (store.CountActiveRecallProxiesForSelection(document.SourceId, document.DocumentId) > 0)
                {
                    skipped.Add(document.DocumentId);
                }
            }

            if (skipped.Count > 0)
            {
                warnings.Add($"Active proxies already exist for {skipped.Count} document(s); rerun with --rebuild to replace them: {string.Join(", ", skipped)}");
                documents = documents.Where(document => !skipped.Contains(document.DocumentId, StringComparer.Ordinal)).ToArray();
            }
        }

        var superseded = request.Rebuild ? store.SupersedeActiveRecallProxies(request.SourceId, request.DocumentId) : 0;
        var proxiesCreated = 0;
        var pointersCreated = 0;

        foreach (var document in documents)
        {
            var chunks = store.ListChunksByDocument(document.DocumentId);
            if (chunks.Count == 0)
            {
                warnings.Add($"Document '{document.DocumentId}' has no chunks; skipped.");
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var version = now.ToUnixTimeMilliseconds().ToString();

            var anchorChunk = chunks[0];
            var documentPointer = new TiroEvidencePointer(
                $"pointer:corpus_document:{document.DocumentId}:v{version}",
                CorpusLane,
                "chunk",
                document.SourceId,
                document.DocumentId,
                anchorChunk.ChunkId,
                anchorChunk.ChunkId,
                null,
                null,
                null,
                null,
                null,
                ComputeTextHash(anchorChunk.Text),
                now,
                $"{{\"proxy_type\":\"corpus_document\",\"chunk_count\":{chunks.Count}}}");
            store.InsertEvidencePointer(documentPointer);
            pointersCreated++;

            var sourceLabel = string.IsNullOrWhiteSpace(document.SourceName) ? document.DocumentId : document.SourceName;
            var firstHeading = DetectHeading(anchorChunk.Text) ?? Summarize(anchorChunk.Text, 80);
            var documentSummary = $"Document '{sourceLabel}' ({document.DocumentId}) in era '{document.TimeframeOrEra}' with {chunks.Count} chunks. Anchor: {firstHeading}";
            var documentProxy = new TiroRecallProxy(
                $"proxy:corpus_document:{document.DocumentId}:v{version}",
                CorpusLane,
                "corpus_document",
                sourceLabel,
                $"Corpus > {sourceLabel}",
                documentSummary,
                BuildKeywords(sourceLabel, document.DocumentId, documentSummary, anchorChunk.Text),
                BuildEntities(sourceLabel, anchorChunk.Text),
                document.SourceId,
                document.DocumentId,
                null,
                null,
                null,
                documentPointer.PointerId,
                "active",
                now,
                now,
                $"{{\"document_id\":\"{JsonEscape(document.DocumentId)}\",\"chunk_count\":{chunks.Count}}}");
            store.InsertRecallProxy(documentProxy);
            proxiesCreated++;

            foreach (var chunk in chunks)
            {
                var heading = DetectHeading(chunk.Text);
                var title = heading is null
                    ? $"{sourceLabel} chunk {chunk.ChunkIndex}/{chunk.ChunkCount}"
                    : $"{heading} ({sourceLabel} chunk {chunk.ChunkIndex}/{chunk.ChunkCount})";
                var breadcrumb = $"Corpus > {sourceLabel} > chunk {chunk.ChunkIndex}/{chunk.ChunkCount}";
                var summary = Summarize(chunk.Text, 420);
                var pointer = new TiroEvidencePointer(
                    $"pointer:corpus_chunk:{chunk.ChunkId}:v{version}",
                    CorpusLane,
                    "chunk",
                    chunk.SourceId,
                    chunk.DocumentId,
                    chunk.ChunkId,
                    chunk.ChunkId,
                    null,
                    null,
                    null,
                    null,
                    null,
                    ComputeTextHash(chunk.Text),
                    now,
                    $"{{\"chunk_index\":{chunk.ChunkIndex},\"chunk_count\":{chunk.ChunkCount}}}");
                store.InsertEvidencePointer(pointer);
                pointersCreated++;

                var proxy = new TiroRecallProxy(
                    $"proxy:corpus_chunk:{chunk.ChunkId}:v{version}",
                    CorpusLane,
                    "corpus_chunk_group",
                    title,
                    breadcrumb,
                    summary,
                    BuildKeywords(title, chunk.SourceName, chunk.DocumentId, breadcrumb, chunk.Text),
                    BuildEntities(title, chunk.Text),
                    chunk.SourceId,
                    chunk.DocumentId,
                    null,
                    null,
                    null,
                    pointer.PointerId,
                    "active",
                    now,
                    now,
                    $"{{\"chunk_id\":\"{JsonEscape(chunk.ChunkId)}\",\"chunk_index\":{chunk.ChunkIndex}}}");
                store.InsertRecallProxy(proxy);
                proxiesCreated++;
            }
        }

        return new TiroProxyBuildResponse("ok", store.DatabasePath, request.Mode, request.SourceId, request.DocumentId, proxiesCreated, superseded, pointersCreated, warnings, errors);
    }

    public TiroProxyInspectResponse Inspect(string databasePath, string? lane, string? documentId, string? sourceId, string? status, int limit)
    {
        using var store = TiroStore.Open(databasePath);
        store.InitializeSchema();
        var normalizedLimit = Math.Clamp(limit <= 0 ? 20 : limit, 1, 200);
        var proxies = store.ListRecallProxies(lane, documentId, sourceId, status, normalizedLimit);
        return new TiroProxyInspectResponse(
            "ok",
            store.DatabasePath,
            new TiroProxyInspectFilters(lane, documentId, sourceId, status, normalizedLimit),
            proxies.Count,
            proxies,
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    public object Hydrate(string databasePath, string pointerId)
    {
        using var store = TiroStore.Open(databasePath);
        store.InitializeSchema();
        var pointer = store.GetEvidencePointer(pointerId);
        if (pointer is null)
        {
            return new
            {
                status = "not_found",
                db_path = store.DatabasePath,
                pointer_id = pointerId,
                warnings = Array.Empty<string>(),
                errors = Array.Empty<string>()
            };
        }

        var warnings = new List<string>();
        var evidence = HydratePointer(store, pointer, null, warnings);
        return new
        {
            status = evidence is null ? "hydration_failed" : "ok",
            db_path = store.DatabasePath,
            pointer_id = pointerId,
            evidence,
            warnings,
            errors = Array.Empty<string>()
        };
    }

    public TiroProxyRecallResponse Recall(TiroProxyRecallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new TiroProxyRecallResponse("validation_error", Path.GetFullPath(request.DatabasePath), string.Empty, Array.Empty<string>(), 0, 0, Array.Empty<TiroRecallProxy>(), Array.Empty<TiroHydratedPointerEvidence>(), Array.Empty<string>(), Array.Empty<string>(), new[] { "query is required." });
        }

        using var store = TiroStore.Open(request.DatabasePath);
        store.InitializeSchema();
        var terms = LexicalSearch.Tokenize(request.Query);
        var warnings = new List<string>();
        var unknowns = new List<string>();
        var limit = Math.Clamp(request.Limit <= 0 ? 5 : request.Limit, 1, 50);

        var scored = store.ListRecallProxiesForSearch()
            .Select(proxy => new { Proxy = proxy, Score = ScoreProxy(proxy, request.Query, terms) })
            .Where(item => item.Score > int.MinValue / 2)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Proxy.Status, StringComparer.Ordinal)
            .ThenBy(item => item.Proxy.ProxyId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        var hydrated = new List<TiroHydratedPointerEvidence>();
        foreach (var item in scored)
        {
            var pointer = store.GetEvidencePointer(item.Proxy.PointerId);
            if (pointer is null)
            {
                warnings.Add($"Proxy '{item.Proxy.ProxyId}' matched but pointer '{item.Proxy.PointerId}' was missing.");
                continue;
            }

            var evidence = HydratePointer(store, pointer, item.Proxy.ProxyId, warnings);
            if (evidence is null)
            {
                warnings.Add($"Proxy '{item.Proxy.ProxyId}' matched but hydration failed for pointer '{pointer.PointerId}'.");
                continue;
            }

            hydrated.Add(evidence);
        }

        if (scored.Length == 0 || hydrated.Count == 0)
        {
            unknowns.Add("No proxy-backed authoritative evidence matched the query.");
        }

        return new TiroProxyRecallResponse(
            "ok",
            store.DatabasePath,
            request.Query.Trim(),
            terms,
            scored.Length,
            hydrated.Count,
            scored.Select(item => item.Proxy).ToArray(),
            hydrated,
            warnings,
            unknowns,
            Array.Empty<string>());
    }

    private static TiroHydratedPointerEvidence? HydratePointer(TiroStore store, TiroEvidencePointer pointer, string? proxyId, List<string> warnings)
    {
        if (pointer.TargetLane == CorpusLane && pointer.TargetKind is "chunk" or "chunk_range")
        {
            if (string.IsNullOrWhiteSpace(pointer.DocumentId) || string.IsNullOrWhiteSpace(pointer.ChunkIdStart))
            {
                warnings.Add($"Pointer '{pointer.PointerId}' is missing corpus document/chunk coordinates.");
                return null;
            }

            var chunks = store.GetChunkRange(pointer.DocumentId, pointer.ChunkIdStart, pointer.ChunkIdEnd);
            if (chunks.Count == 0)
            {
                return null;
            }

            var text = string.Join("\n\n", chunks.Select(chunk => chunk.Text));
            var hash = ComputeTextHash(text);
            if (!string.Equals(hash, pointer.TextHash, StringComparison.Ordinal))
            {
                warnings.Add($"Hydration hash mismatch for pointer '{pointer.PointerId}'. Stored hash kept; hydrated text may reflect source drift.");
            }

            return new TiroHydratedPointerEvidence(
                pointer.PointerId,
                proxyId,
                pointer.TargetLane,
                pointer.TargetKind,
                pointer.SourceId,
                pointer.DocumentId,
                pointer.SessionId,
                pointer.RecordId,
                pointer.FactId,
                chunks.Select(chunk => chunk.ChunkId).ToArray(),
                Array.Empty<long>(),
                text,
                pointer.TextHash,
                chunks.MaxBy(chunk => chunk.IngestedUtc)?.IngestedUtc,
                $"Corpus chunks {chunks[0].ChunkIndex}-{chunks[^1].ChunkIndex} from document '{pointer.DocumentId}'.");
        }

        warnings.Add($"Pointer target kind '{pointer.TargetKind}' is not implemented in M022.");
        return null;
    }

    private static int ScoreProxy(TiroRecallProxy proxy, string query, IReadOnlyList<string> terms)
    {
        if (proxy.Status == "hidden")
        {
            return int.MinValue;
        }

        var lexical = LexicalSearch.Score(proxy.Summary, $"{proxy.Title} {proxy.Breadcrumb} {proxy.Keywords} {proxy.Entities} {proxy.MetadataJson}", terms);
        if (lexical.Score <= 0)
        {
            return int.MinValue / 2;
        }

        var score = lexical.Score;
        score += BoostField(proxy.Title, terms, 30);
        score += BoostField(proxy.Breadcrumb, terms, 16);
        score += BoostField(proxy.Keywords, terms, 18);
        score += BoostField(proxy.Entities, terms, 14);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var trimmed = query.Trim();
            if (proxy.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                score += 30;
            }
            if (proxy.Breadcrumb.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }
            if (proxy.Summary.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                score += 12;
            }
        }

        if (proxy.ProxyType == "corpus_document")
        {
            score += 18;
        }

        score += proxy.Status switch
        {
            "active" => 8,
            "stale" => -12,
            "superseded" => -30,
            _ => 0
        };

        return score;
    }

    private static int BoostField(string field, IReadOnlyList<string> terms, int perTerm)
    {
        if (string.IsNullOrWhiteSpace(field) || terms.Count == 0)
        {
            return 0;
        }

        var matched = terms.Count(term => field.Contains(term, StringComparison.OrdinalIgnoreCase));
        return matched * perTerm;
    }

    private static string? DetectHeading(string text)
    {
        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(6))
        {
            var line = rawLine.Trim();
            if (line.Length is < 4 or > 90)
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                return line.Trim('[', ']');
            }

            if (line is "Introduction" or "Writing an Exploit" or "Appendix")
            {
                return line;
            }

            if (char.IsUpper(line[0]) && line.Count(char.IsLetter) >= 4 && !line.EndsWith(".", StringComparison.Ordinal))
            {
                return line;
            }
        }

        return null;
    }

    private static string Summarize(string text, int maxChars)
    {
        var clean = NormalizeWhitespace(text);
        return clean.Length <= maxChars ? clean : clean[..maxChars].TrimEnd() + "...";
    }

    private static string BuildKeywords(params string[] values)
    {
        return string.Join(' ', values
            .SelectMany(LexicalSearch.Tokenize)
            .Distinct(StringComparer.Ordinal)
            .Take(40));
    }

    private static string BuildEntities(params string[] values)
    {
        var entities = new List<string>();
        foreach (var value in values)
        {
            foreach (var token in value.Split(new[] { ' ', '\n', '\r', '\t', ',', ':', ';', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = token.Trim('.', '"', '\'', '-', '_');
                if (trimmed.Length >= 3 && char.IsUpper(trimmed[0]))
                {
                    entities.Add(trimmed);
                }
            }
        }

        return string.Join(' ', entities.Distinct(StringComparer.Ordinal).Take(20));
    }

    private static string NormalizeWhitespace(string text)
    {
        return string.Join(' ', (text ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string ComputeTextHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string JsonEscape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
