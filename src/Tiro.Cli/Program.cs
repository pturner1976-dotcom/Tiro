using System.Text.Json;
using System.Reflection;
using Tiro.Cli;

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true
};

using var SharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

try
{
    var options = CliOptions.Parse(args);
    if (IsInitCommand(options.Command))
    {
        var response = InitializeStore(options.DatabasePath);
        Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
        return;
    }

    if (options.Command == "query")
    {
        RequireArgument(options, "query <query>");
        var queryRequest = new TiroQueryRequest(
            options.DatabasePath,
            string.Join(' ', options.Arguments),
            options.Limit,
            options.Filters.SourceId,
            options.Filters.DocumentId,
            options.ContextWindow,
            options.SessionId,
            options.PlannerMode,
            options.DebugPlanner);
        var queryResponse = await new TiroQueryService().QueryAsync(queryRequest, CancellationToken.None);
        Console.Error.WriteLine($"Planner key {queryResponse.PlannerKeyName} found: {queryResponse.PlannerKeyFound}");
        Console.WriteLine(JsonSerializer.Serialize(queryResponse.Packet, jsonOptions));
        return;
    }

    if (options.Command == "ingest-session-note")
    {
        var commandOptions = ParseCommandOptions(options.Arguments);
        var response = new TiroIngestStateService().IngestSessionNote(new TiroSessionNoteIngestRequest(
            options.DatabasePath,
            options.SessionId ?? GetRequired(commandOptions, "session-id"),
            GetRequired(commandOptions, "text"),
            GetRequired(commandOptions, "source-identity"),
            GetValue(commandOptions, "direction") ?? "operator",
            GetTimestamp(commandOptions, "timestamp-utc")));
        Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
        return;
    }

    if (options.Command == "ingest-operational-record")
    {
        var commandOptions = ParseCommandOptions(options.Arguments);
        var response = new TiroIngestStateService().IngestOperationalRecord(new TiroOperationalRecordIngestRequest(
            options.DatabasePath,
            GetRequired(commandOptions, "record-type"),
            GetRequired(commandOptions, "text"),
            GetRequired(commandOptions, "source-identity"),
            options.SessionId ?? GetValue(commandOptions, "session-id"),
            GetTimestamp(commandOptions, "timestamp-utc")));
        Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
        return;
    }

    if (options.Command == "ingest-aichat-session")
    {
        var commandOptions = ParseCommandOptions(options.Arguments);
        var response = new TiroIngestStateService().IngestAichatSession(new TiroAichatSessionIngestRequest(
            options.DatabasePath,
            options.SessionId ?? GetValue(commandOptions, "session-id"),
            GetValue(commandOptions, "file"),
            GetRequired(commandOptions, "source-identity"),
            GetTimestamp(commandOptions, "timestamp-utc"),
            GetInt(commandOptions, "max-chars") ?? 50000,
            GetBool(commandOptions, "latest"),
            GetValue(commandOptions, "sessions-dir"),
            GetBool(commandOptions, "force")));
        Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
        return;
    }

    if (options.Command == "inspect-aichat-sessions")
    {
        var commandOptions = ParseCommandOptions(options.Arguments);
        var inspection = AichatSessionDiscovery.Inspect(GetValue(commandOptions, "sessions-dir"));
        Console.WriteLine(JsonSerializer.Serialize(inspection, jsonOptions));
        return;
    }

    if (options.Command == "search-debug")
    {
        RequireArgument(options, "search-debug <query>");
        var diagnostics = await new RetrievalDiagnostics().SearchDebugAsync(new TiroSearchDebugRequest(
            options.DatabasePath,
            string.Join(' ', options.Arguments),
            options.Limit,
            options.Filters.SourceId,
            options.Filters.DocumentId,
            options.SessionId,
            options.PlannerMode,
            options.DebugPlanner), CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(diagnostics, jsonOptions));
        return;
    }

    if (options.Command == "inspect")
    {
        RequireArgument(options, "inspect sessions|operational|recent|stats|sources|documents|proxies");
        var inspectService = new TiroInspectService();
        var proxyService = new TiroProxyService();
        var mode = options.Arguments[0].Trim().ToLowerInvariant();
        var commandOptions = ParseCommandOptions(options.Arguments.Skip(1).ToArray());
        switch (mode)
        {
            case "sessions":
                Console.WriteLine(JsonSerializer.Serialize(inspectService.InspectSessions(options.DatabasePath, GetInt(commandOptions, "limit") ?? options.Limit), jsonOptions));
                return;
            case "operational":
                Console.WriteLine(JsonSerializer.Serialize(inspectService.InspectOperational(
                    options.DatabasePath,
                    GetValue(commandOptions, "record-type"),
                    options.SessionId ?? GetValue(commandOptions, "session-id"),
                    GetInt(commandOptions, "limit") ?? options.Limit), jsonOptions));
                return;
            case "sources":
                Console.WriteLine(JsonSerializer.Serialize(inspectService.InspectSources(options.DatabasePath, GetInt(commandOptions, "limit") ?? options.Limit), jsonOptions));
                return;
            case "documents":
                Console.WriteLine(JsonSerializer.Serialize(inspectService.InspectDocuments(
                    options.DatabasePath,
                    options.Filters.SourceId ?? GetValue(commandOptions, "source-id"),
                    GetInt(commandOptions, "limit") ?? options.Limit), jsonOptions));
                return;
            case "recent":
                Console.WriteLine(JsonSerializer.Serialize(inspectService.InspectRecent(options.DatabasePath, GetInt(commandOptions, "limit") ?? options.Limit), jsonOptions));
                return;
            case "stats":
                Console.WriteLine(JsonSerializer.Serialize(inspectService.InspectStats(options.DatabasePath), jsonOptions));
                return;
            case "proxies":
                Console.WriteLine(JsonSerializer.Serialize(proxyService.Inspect(
                    options.DatabasePath,
                    GetValue(commandOptions, "lane"),
                    options.Filters.DocumentId ?? GetValue(commandOptions, "document-id"),
                    options.Filters.SourceId ?? GetValue(commandOptions, "source-id"),
                    GetValue(commandOptions, "status"),
                    GetInt(commandOptions, "limit") ?? options.Limit), jsonOptions));
                return;
            default:
                throw new InvalidOperationException("Usage: tiro inspect sessions|operational|recent|stats|sources|documents|proxies");
        }
    }

    if (options.Command == "session-search")
    {
        RequireArgument(options, "session-search <query>");
        var commandOptions = ParseCommandOptions(options.Arguments.Skip(1).ToArray());
        var response = new TiroInspectService().SessionSearch(
            options.DatabasePath,
            options.Arguments[0],
            options.Limit,
            GetInt(commandOptions, "session-limit") ?? 10);
        Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
        return;
    }

    if (options.Command == "recall")
    {
        RequireArgument(options, "recall <query>");
        var commandOptions = ParseCommandOptions(options.Arguments.Skip(1).ToArray());
        var response = new TiroRecallService().Recall(new TiroRecallRequest(
            options.DatabasePath,
            options.Arguments[0],
            options.Limit,
            options.PlannerMode,
            options.DebugPlanner,
            GetInt(commandOptions, "session-limit") ?? 10,
            GetInt(commandOptions, "source-limit") ?? 20,
            GetInt(commandOptions, "document-limit") ?? 20));
        Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
        return;
    }

    if (options.Command == "proxy-build")
    {
        RequireArgument(options, "proxy-build corpus [--document-id <id>] [--source-id <id>] [--rebuild]");
        var mode = options.Arguments[0].Trim().ToLowerInvariant();
        var commandOptions = ParseCommandOptions(options.Arguments.Skip(1).ToArray());
        var response = mode switch
        {
            "corpus" => new TiroProxyService().BuildCorpusProxies(new TiroProxyBuildRequest(
                options.DatabasePath,
                mode,
                options.Filters.DocumentId ?? GetValue(commandOptions, "document-id"),
                options.Filters.SourceId ?? GetValue(commandOptions, "source-id"),
                GetBool(commandOptions, "rebuild"))),
            _ => throw new InvalidOperationException("Usage: tiro proxy-build corpus [--document-id <id>] [--source-id <id>] [--rebuild]")
        };
        Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
        return;
    }

    if (options.Command == "proxy-inspect")
    {
        var commandOptions = ParseCommandOptions(options.Arguments);
        var response = new TiroProxyService().Inspect(
            options.DatabasePath,
            GetValue(commandOptions, "lane"),
            options.Filters.DocumentId ?? GetValue(commandOptions, "document-id"),
            options.Filters.SourceId ?? GetValue(commandOptions, "source-id"),
            GetValue(commandOptions, "status"),
            GetInt(commandOptions, "limit") ?? options.Limit);
        Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
        return;
    }

    if (options.Command == "proxy-recall")
    {
        RequireArgument(options, "proxy-recall <query>");
        var response = new TiroProxyService().Recall(new TiroProxyRecallRequest(
            options.DatabasePath,
            options.Arguments[0],
            options.Limit));
        Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
        return;
    }

    if (options.Command == "proxy-hydrate")
    {
        RequireArgument(options, "proxy-hydrate <pointer_id>");
        var response = new TiroProxyService().Hydrate(options.DatabasePath, options.Arguments[0]);
        Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
        return;
    }

    if (options.Command == "session-summary")
    {
        RequireArgument(options, "session-summary <session_id>");
        var response = new TiroInspectService().SessionSummary(options.DatabasePath, options.Arguments[0], options.Limit);
        Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
        return;
    }

    if (options.Command == "phrase-search")
    {
        RequireArgument(options, "phrase-search <phrase>");
        var commandOptions = ParseCommandOptions(options.Arguments.Skip(1).ToArray());
        var response = new TiroInspectService().PhraseSearch(
            options.DatabasePath,
            options.Arguments[0],
            GetValue(commandOptions, "lane") ?? "all",
            options.SessionId ?? GetValue(commandOptions, "session-id"),
            GetInt(commandOptions, "limit") ?? options.Limit);
        Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
        return;
    }

    if (options.Command == "proof-carry-forward")
    {
        var commandOptions = ParseCommandOptions(options.Arguments);
        var phrase = GetValue(commandOptions, "phrase")
            ?? $"proof carry forward alpha m013proofcarryforward{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var sourceIdentity = GetValue(commandOptions, "source-identity") ?? "cli:proof-carry-forward";
        var recordType = GetValue(commandOptions, "record-type") ?? "decision";
        var queryText = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
        var queryService = new TiroQueryService();
        var before = await queryService.QueryAsync(new TiroQueryRequest(
            options.DatabasePath,
            queryText,
            options.Limit,
            null,
            null,
            0,
            options.SessionId,
            PlannerMode.Off,
            false), CancellationToken.None);
        var ingest = new TiroIngestStateService().IngestOperationalRecord(new TiroOperationalRecordIngestRequest(
            options.DatabasePath,
            recordType,
            phrase,
            sourceIdentity,
            options.SessionId));
        var after = await queryService.QueryAsync(new TiroQueryRequest(
            options.DatabasePath,
            queryText,
            options.Limit,
            null,
            null,
            0,
            options.SessionId,
            PlannerMode.Off,
            false), CancellationToken.None);
        var freshProcess = await new TiroQueryService().QueryAsync(new TiroQueryRequest(
            options.DatabasePath,
            queryText,
            options.Limit,
            null,
            null,
            0,
            options.SessionId,
            PlannerMode.Off,
            false), CancellationToken.None);

        var proof = new TiroProofCarryForwardResult(
            CountEvidence(before.Packet) == 0 && CountEvidence(after.Packet) > 0 && CountEvidence(freshProcess.Packet) > 0
                ? "PASS"
                : "FAIL",
            after.DatabasePath,
            phrase,
            recordType,
            CountEvidence(before.Packet),
            CountEvidence(after.Packet),
            CountEvidence(freshProcess.Packet),
            ingest.WrittenItems.FirstOrDefault()?.Id ?? 0,
            after.Packet.Warnings.Select(warning => warning.Text).ToArray());
        Console.WriteLine(JsonSerializer.Serialize(proof, jsonOptions));
        return;
    }

    if (options.Command == "semantic-status")
    {
        var statusResponse = new SemanticStatusService().GetStatus(options.DatabasePath, EmbeddingConfig.Load());
        Console.WriteLine(JsonSerializer.Serialize(statusResponse, jsonOptions));
        return;
    }

    if (options.Command == "semantic-index")
    {
        var commandOptions = ParseCommandOptions(options.Arguments);
        var lanes = ParseLanes(GetValue(commandOptions, "lanes"));
        var rebuild = GetBool(commandOptions, "rebuild");
        var dryRun = GetBool(commandOptions, "dry-run");
        var indexLimit = GetInt(commandOptions, "limit");
        var embeddingConfig = EmbeddingConfig.Load();

        if (!dryRun && !embeddingConfig.Enabled)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new SemanticIndexResponse(
                    "disabled",
                    new SemanticIndexRunSummary(string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, embeddingConfig.Provider, embeddingConfig.Model, string.Join(",", lanes), 0, 0, 0, 0, false, "disabled"),
                    new[] { "Semantic search is disabled (TIRO_SEMANTIC_ENABLED is not \"true\"); no provider call was made. Use --dry-run to preview candidates without enabling semantic search." }),
                jsonOptions));
            return;
        }

        IEmbeddingClient? indexClient = null;
        if (!dryRun)
        {
            var apiKey = embeddingConfig.GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    new SemanticIndexResponse(
                        "validation_error",
                        new SemanticIndexRunSummary(string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, embeddingConfig.Provider, embeddingConfig.Model, string.Join(",", lanes), 0, 0, 0, 0, false, "validation_error"),
                        new[] { $"Embedding provider ({embeddingConfig.Provider}) key ({embeddingConfig.KeyName}) is not configured; no provider call was made." }),
                    jsonOptions));
                return;
            }

            indexClient = new OpenAiEmbeddingClient(SharedHttpClient, apiKey, embeddingConfig.Model);
        }

        var indexResponse = await new SemanticIndexService().IndexAsync(
            options.DatabasePath, lanes, rebuild, dryRun, indexLimit, embeddingConfig, indexClient, CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(indexResponse, jsonOptions));
        return;
    }

    if (options.Command == "semantic-query")
    {
        RequireArgument(options, "semantic-query <query> [--limit <n>] [--min-score <f>] [--lanes corpus,session]");
        var commandOptions = ParseCommandOptions(options.Arguments.Skip(1).ToArray());
        var semanticConfig = EmbeddingConfig.Load();
        var lanes = ParseLanes(GetValue(commandOptions, "lanes"));
        var minScore = GetDouble(commandOptions, "min-score") ?? semanticConfig.MinScore;
        IEmbeddingClient? queryClient = null;
        var apiKey = semanticConfig.GetApiKey();
        if (semanticConfig.Enabled && !string.IsNullOrWhiteSpace(apiKey))
        {
            queryClient = new OpenAiEmbeddingClient(SharedHttpClient, apiKey, semanticConfig.Model);
        }

        var semanticResponse = await new SemanticSearchService().SearchAsync(
            options.DatabasePath, options.Arguments[0], options.Limit, minScore, lanes, semanticConfig, queryClient, CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(semanticResponse, jsonOptions));
        return;
    }

    if (options.Command == "hybrid-search")
    {
        RequireArgument(options, "hybrid-search <query> [--limit <n>] [--lanes corpus,session] [--lexical-weight <f>] [--semantic-weight <f>] [--min-semantic-score <f>]");
        var commandOptions = ParseCommandOptions(options.Arguments.Skip(1).ToArray());
        var hybridConfig = EmbeddingConfig.Load();
        var lanes = ParseLanes(GetValue(commandOptions, "lanes"));
        var lexicalWeight = GetDouble(commandOptions, "lexical-weight") ?? 0.55;
        var semanticWeight = GetDouble(commandOptions, "semantic-weight") ?? 0.45;
        var minSemanticScore = GetDouble(commandOptions, "min-semantic-score") ?? hybridConfig.MinScore;
        IEmbeddingClient? hybridClient = null;
        var hybridApiKey = hybridConfig.GetApiKey();
        if (hybridConfig.Enabled && !string.IsNullOrWhiteSpace(hybridApiKey))
        {
            hybridClient = new OpenAiEmbeddingClient(SharedHttpClient, hybridApiKey, hybridConfig.Model);
        }

        var hybridResponse = await new HybridSearchService().SearchAsync(
            options.DatabasePath, options.Arguments[0], options.Limit, lanes, lexicalWeight, semanticWeight, minSemanticScore, hybridConfig, hybridClient, CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(hybridResponse, jsonOptions));
        return;
    }

    using var store = TiroStore.Open(options.DatabasePath);

    switch (options.Command)
    {
        case "fact-add":
            if (options.Arguments.Count < 3)
            {
                throw new InvalidOperationException("Usage: tiro fact-add <text> <source_id> <origin_identity> [--session-id <id>] [--status <status>]");
            }
            {
                var text = options.Arguments[0];
                var sourceId = options.Arguments[1];
                var originIdentity = options.Arguments[2];
                var status = TiroStore.FactStatus.Active;
                for (int i = 3; i < options.Arguments.Count; i++)
                {
                    if (options.Arguments[i] == "--status" && i + 1 < options.Arguments.Count)
                    {
                        var st = options.Arguments[i + 1].Trim().ToLowerInvariant();
                        if (!TiroStore.FactStatus.IsValid(st))
                        {
                            throw new InvalidOperationException($"Invalid fact status: {st}");
                        }
                        status = st;
                        i++;
                    }
                }
                var fact = store.AddFact(text, sourceId, originIdentity, status, options.SessionId);
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(fact, jsonOptions));
            }
            break;
        case "fact-list":
            {
                string? statusFilter = null;
                int limit = options.Limit > 0 ? options.Limit : 100;
                for (int i = 0; i < options.Arguments.Count; i++)
                {
                    if (options.Arguments[i] == "--status" && i + 1 < options.Arguments.Count)
                    {
                        var st = options.Arguments[i + 1].Trim().ToLowerInvariant();
                        if (!TiroStore.FactStatus.IsValid(st))
                        {
                            throw new InvalidOperationException($"Invalid fact status filter: {st}");
                        }
                        statusFilter = st;
                        i++;
                    }
                }
                var facts = store.ListFacts(statusFilter, limit);
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(facts, jsonOptions));
            }
            break;
        case "fact-update-status":
            if (options.Arguments.Count < 2)
            {
                throw new InvalidOperationException("Usage: tiro fact-update-status <fact_id> <new_status>");
            }
            {
                if (!int.TryParse(options.Arguments[0], out var factId))
                {
                    throw new InvalidOperationException("fact_id must be an integer.");
                }
                var newStatus = options.Arguments[1].Trim().ToLowerInvariant();
                if (!TiroStore.FactStatus.IsValid(newStatus))
                {
                    throw new InvalidOperationException($"Invalid fact status: {newStatus}");
                }
                var updated = store.UpdateFactStatus(factId, newStatus);
                Console.WriteLine(updated ? "Fact status updated." : "Fact not found or status unchanged.");
            }
            break;
        case "fact-supersede":
            if (options.Arguments.Count < 2)
            {
                throw new InvalidOperationException("Usage: tiro fact-supersede <superseding_fact_id> <superseded_fact_id>");
            }
            {
                if (!int.TryParse(options.Arguments[0], out var supersedingFactId))
                {
                    throw new InvalidOperationException("superseding_fact_id must be an integer.");
                }
                if (!int.TryParse(options.Arguments[1], out var supersededFactId))
                {
                    throw new InvalidOperationException("superseded_fact_id must be an integer.");
                }
                var result = store.SupersedeFact(supersedingFactId, supersededFactId);
                Console.WriteLine(result ? "Supersession recorded." : "Supersession failed or no change.");
            }
            break;
        case "fact-conflict":
            if (options.Arguments.Count < 2)
            {
                throw new InvalidOperationException("Usage: tiro fact-conflict <fact_id_1> <fact_id_2>");
            }
            {
                if (!int.TryParse(options.Arguments[0], out var factId1))
                {
                    throw new InvalidOperationException("fact_id_1 must be an integer.");
                }
                if (!int.TryParse(options.Arguments[1], out var factId2))
                {
                    throw new InvalidOperationException("fact_id_2 must be an integer.");
                }
                store.AddFactConflict(factId1, factId2);
                Console.WriteLine("Fact conflict recorded.");
            }
            break;
        case "ingest-chunks":
            RequireArgument(options, "ingest-chunks <path>");
            var report = store.IngestChunks(options.Arguments[0]);
            Console.WriteLine($"Input: {report.InputPath}");
            Console.WriteLine($"Lines read: {report.LinesRead}");
            Console.WriteLine($"Chunks inserted: {report.ChunksInserted}");
            Console.WriteLine($"Duplicate chunks: {report.DuplicateChunks}");
            Console.WriteLine($"Sources registered: {report.SourcesRegistered}");
            Console.WriteLine($"Documents registered: {report.DocumentsRegistered}");
            Console.WriteLine($"Ingested UTC: {report.IngestedUtc:O}");
            break;

        case "session-create":
            RequireArgument(options, "session-create <session_id>");
            Console.WriteLine(JsonSerializer.Serialize(store.CreateSession(options.Arguments[0]), jsonOptions));
            break;

        case "session-ingest":
            if (options.Arguments.Count < 4)
            {
                throw new InvalidOperationException("Usage: tiro session-ingest <session_id> <direction> <source_identity> <text>");
            }
            var ingestReport = store.IngestMessage(
                options.Arguments[0],
                options.Arguments[1],
                options.Arguments[2],
                string.Join(' ', options.Arguments.Skip(3)));
            Console.WriteLine(JsonSerializer.Serialize(ingestReport, jsonOptions));
            break;

        case "session-recent":
            RequireArgument(options, "session-recent <session_id>");
            Console.WriteLine(JsonSerializer.Serialize(store.GetRecentMessages(options.Arguments[0], options.Limit), jsonOptions));
            break;

        case "session-query":
            if (options.Arguments.Count < 2)
            {
                throw new InvalidOperationException("Usage: tiro session-query <session_id> <query>");
            }
            Console.WriteLine(JsonSerializer.Serialize(
                store.QuerySessionMessages(options.Arguments[0], string.Join(' ', options.Arguments.Skip(1)), options.Limit),
                jsonOptions));
            break;

        case "decision-add":
            WriteOperationalAdd(store, "decision", options, jsonOptions);
            break;

        case "todo-add":
            WriteOperationalAdd(store, "todo", options, jsonOptions);
            break;

        case "unknown-add":
            WriteOperationalAdd(store, "unknown", options, jsonOptions);
            break;

        case "warning-add":
            WriteOperationalAdd(store, "warning", options, jsonOptions);
            break;

        case "decision-list":
            WriteOperationalList(store, "decision", options, jsonOptions);
            break;

        case "todo-list":
            WriteOperationalList(store, "todo", options, jsonOptions);
            break;

        case "unknown-list":
            WriteOperationalList(store, "unknown", options, jsonOptions);
            break;

        case "warning-list":
            WriteOperationalList(store, "warning", options, jsonOptions);
            break;

        case "stats":
            var stats = store.GetStats();
            Console.WriteLine(JsonSerializer.Serialize(stats, jsonOptions));
            break;

        case "help":
        case "--help":
        case "-h":
            WriteHelp();
            break;

        case "version":
        case "--version":
        case "-v":
            WriteVersion();
            break;

        default:
            throw new InvalidOperationException($"Unknown command: {options.Command}");
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"tiro: {ex.Message}");
    Environment.ExitCode = 1;
}

static void RequireArgument(CliOptions options, string usage)
{
    if (options.Arguments.Count == 0)
    {
        throw new InvalidOperationException($"Usage: tiro {usage}");
    }
}

static void WriteHelp()
{
    Console.WriteLine("Tiro v3 - derived from the proven Tiro v1 retrieval and memory core");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  tiro [--db <path>] init");
    Console.WriteLine("  tiro [--db <path>] ingest-chunks <path>");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] [--source-id <id>] [--document-id <id>] [--context-window 0..2] [--session-id <id>] [--planner on|off|auto] [--debug-planner] query <query>");
    Console.WriteLine("  tiro [--db <path>] --session-id <id> ingest-session-note --source-identity <identity> --text <text> [--direction user|assistant|operator|system] [--timestamp-utc <iso8601>]");
    Console.WriteLine("  tiro [--db <path>] [--session-id <id>] ingest-operational-record --record-type decision|todo|warning|unknown --source-identity <identity> --text <text> [--timestamp-utc <iso8601>]");
    Console.WriteLine("  tiro [--db <path>] [--session-id <id>] ingest-aichat-session --source-identity <identity> [--file <path>|--latest] [--max-chars <n>] [--timestamp-utc <iso8601>]");
    Console.WriteLine("  tiro inspect-aichat-sessions [--sessions-dir <path>]");
    Console.WriteLine("  tiro [--db <path>] inspect sessions [--limit <n>]");
    Console.WriteLine("  tiro [--db <path>] inspect operational [--record-type decision|todo|warning|unknown] [--session-id <id>] [--limit <n>]");
    Console.WriteLine("  tiro [--db <path>] inspect sources [--limit <n>]");
    Console.WriteLine("  tiro [--db <path>] inspect documents [--source-id <id>] [--limit <n>]");
    Console.WriteLine("  tiro [--db <path>] inspect proxies [--lane <lane>] [--document-id <id>] [--source-id <id>] [--status <status>] [--limit <n>]");
    Console.WriteLine("  tiro [--db <path>] inspect recent [--limit <n>]");
    Console.WriteLine("  tiro [--db <path>] inspect stats");
    Console.WriteLine("  tiro [--db <path>] proxy-build corpus [--document-id <id>] [--source-id <id>] [--rebuild]");
    Console.WriteLine("  tiro [--db <path>] proxy-inspect [--lane <lane>] [--document-id <id>] [--source-id <id>] [--status <status>] [--limit <n>]");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] proxy-recall <query>");
    Console.WriteLine("  tiro [--db <path>] proxy-hydrate <pointer_id>");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] session-search <query> [--session-limit <n>]");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] [--planner on|off|auto] [--debug-planner] recall <query> [--session-limit <n>] [--source-limit <n>] [--document-limit <n>]");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] session-summary <session_id>");
    Console.WriteLine("  tiro [--db <path>] [--session-id <id>] [--limit <n>] phrase-search <phrase> [--lane session|operational|corpus|facts|all]");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] [--source-id <id>] [--document-id <id>] [--session-id <id>] [--planner on|off|auto] [--debug-planner] search-debug <query>");
    Console.WriteLine("  tiro [--db <path>] [--session-id <id>] proof-carry-forward [--phrase <text>] [--record-type decision|todo|warning|unknown] [--source-identity <identity>]");
    Console.WriteLine("  tiro [--db <path>] session-create <session_id>");
    Console.WriteLine("  tiro [--db <path>] session-ingest <session_id> <direction> <source_identity> <text>");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] session-recent <session_id>");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] session-query <session_id> <query>");
    Console.WriteLine("  tiro [--db <path>] [--session-id <id>] decision-add <origin> <text>");
    Console.WriteLine("  tiro [--db <path>] [--session-id <id>] todo-add <origin> <text>");
    Console.WriteLine("  tiro [--db <path>] [--session-id <id>] unknown-add <origin> <text>");
    Console.WriteLine("  tiro [--db <path>] [--session-id <id>] warning-add <origin> <text>");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] [--session-id <id>] decision-list");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] [--session-id <id>] todo-list");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] [--session-id <id>] unknown-list");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] [--session-id <id>] warning-list");
    Console.WriteLine("  tiro [--db <path>] [--session-id <id>] fact-add <text> <source_id> <origin_identity> [--status active|stale|superseded|conflicting|unknown]");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] fact-list [--status active|stale|superseded|conflicting|unknown]");
    Console.WriteLine("  tiro [--db <path>] fact-update-status <fact_id> <new_status>");
    Console.WriteLine("  tiro [--db <path>] fact-supersede <superseding_fact_id> <superseded_fact_id>");
    Console.WriteLine("  tiro [--db <path>] fact-conflict <fact_id_1> <fact_id_2>");
    Console.WriteLine("  tiro [--db <path>] stats");
    Console.WriteLine("  tiro [--db <path>] semantic-status");
    Console.WriteLine("  tiro [--db <path>] semantic-index [--lanes corpus,session] [--rebuild] [--dry-run] [--limit <n>]");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] semantic-query <query> [--min-score <f>] [--lanes corpus,session]");
    Console.WriteLine("  tiro [--db <path>] [--limit <n>] hybrid-search <query> [--lanes corpus,session] [--lexical-weight <f>] [--semantic-weight <f>] [--min-semantic-score <f>]");
    Console.WriteLine("  tiro version");
    Console.WriteLine();
    Console.WriteLine("Notes:");
    Console.WriteLine("  Retrieval is deterministic normalized lexical matching with optional Gemini query planning.");
    Console.WriteLine("  Query filters are explicit and deterministic; filtered no-match is not broadened.");
    Console.WriteLine("  Adjacent context windows are bounded to 0..2 chunks and preserve chunk provenance.");
    Console.WriteLine("  Session messages are explicit state memory, stored separately from corpus chunks.");
    Console.WriteLine("  Operational memory records are explicit decisions, TODOs, unknowns, and warnings.");
    Console.WriteLine("  Fact lifecycle memory is explicit and separate from corpus, session, and operational memory.");
    Console.WriteLine("  Query packets include deterministic retrieval_policy signals for relevance, recency, importance, lifecycle, and mode weighting.");
    Console.WriteLine("  Planner secrets use GEMINI_API_KEY from process env, then ~/.env/Tiro_v1.env.");
    Console.WriteLine("  Planner model defaults to gemini-2.5-flash; override with GEMINI_PLANNER_MODEL.");
    Console.WriteLine("  Query output is a structured context packet with source provenance.");
    Console.WriteLine("  Semantic search is native v3 vector retrieval, separate from lexical search and the planner; disabled by default via TIRO_SEMANTIC_ENABLED.");
    Console.WriteLine("  Semantic/hybrid commands never call an embedding provider unless TIRO_SEMANTIC_ENABLED=true and a provider key is configured.");
}

static void WriteVersion()
{
    var assembly = Assembly.GetExecutingAssembly();
    var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";
    Console.WriteLine($"Tiro v3 {informational}");
}

static bool IsInitCommand(string command) =>
    string.Equals(command, "init", StringComparison.OrdinalIgnoreCase);

static TiroInitResponse InitializeStore(string databasePath)
{
    if (string.IsNullOrWhiteSpace(databasePath))
    {
        throw new InvalidOperationException("Database path is required.");
    }

    var fullPath = Path.GetFullPath(databasePath);
    var directory = Path.GetDirectoryName(fullPath)
        ?? throw new InvalidOperationException("Database path must include a directory.");
    if (!Directory.Exists(directory))
    {
        throw new InvalidOperationException($"Parent directory does not exist: {directory}");
    }
    if (File.Exists(fullPath))
    {
        throw new InvalidOperationException($"Database already exists: {fullPath}");
    }

    using var store = TiroStore.Open(fullPath);
    store.InitializeSchema();
    try
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
    catch (PlatformNotSupportedException)
    {
        // Ignore on platforms without Unix file mode support.
    }

    var assembly = Assembly.GetExecutingAssembly();
    var productVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";

    return new TiroInitResponse(
        "ok",
        store.DatabasePath,
        store.GetSchemaVersion(),
        productVersion,
        true,
        store.ListTableNames(),
        store.ListIndexNames(),
        store.GetInitRecordCounts());
}

static Dictionary<string, string> ParseCommandOptions(IReadOnlyList<string> arguments)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < arguments.Count; index++)
    {
        var argument = arguments[index];
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected positional argument: {argument}");
        }

        var key = argument[2..];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Option name is required.");
        }

        if (key is "latest" or "force" or "rebuild" or "dry-run")
        {
            result[key] = "true";
            continue;
        }

        if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"--{key} requires a value.");
        }

        result[key] = arguments[++index];
    }

    return result;
}

static string GetRequired(IReadOnlyDictionary<string, string> options, string key)
{
    var value = GetValue(options, key);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"--{key} is required.");
    }

    return value;
}

static string? GetValue(IReadOnlyDictionary<string, string> options, string key)
{
    return options.TryGetValue(key, out var value) ? value : null;
}

static DateTimeOffset? GetTimestamp(IReadOnlyDictionary<string, string> options, string key)
{
    var value = GetValue(options, key);
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    if (!DateTimeOffset.TryParse(value, out var timestamp))
    {
        throw new InvalidOperationException($"--{key} must be an ISO-8601 timestamp.");
    }

    return timestamp;
}

static int? GetInt(IReadOnlyDictionary<string, string> options, string key)
{
    var value = GetValue(options, key);
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    if (!int.TryParse(value, out var parsed))
    {
        throw new InvalidOperationException($"--{key} must be an integer.");
    }

    return parsed;
}

static bool GetBool(IReadOnlyDictionary<string, string> options, string key)
{
    return options.TryGetValue(key, out var value)
        && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.Ordinal)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
}

static double? GetDouble(IReadOnlyDictionary<string, string> options, string key)
{
    var value = GetValue(options, key);
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    if (!double.TryParse(value, out var parsed))
    {
        throw new InvalidOperationException($"--{key} must be a number.");
    }

    return parsed;
}

static IReadOnlyList<string> ParseLanes(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return new[] { "corpus", "session" };
    }

    var lanes = value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(lane => lane.ToLowerInvariant())
        .Where(lane => lane is "corpus" or "session")
        .Distinct()
        .ToArray();

    if (lanes.Length == 0)
    {
        throw new InvalidOperationException("--lanes must include at least one of: corpus, session.");
    }

    return lanes;
}

static int CountEvidence(ContextPacket packet)
{
    return packet.PrimaryEvidence.Count
        + packet.SessionEvidence.Count
        + packet.OperationalMemory.Count
        + packet.Facts.Count(fact => fact.EvidenceType == "fact-lifecycle");
}

static void WriteOperationalAdd(TiroStore store, string recordType, CliOptions options, JsonSerializerOptions jsonOptions)
{
    if (options.Arguments.Count < 2)
    {
        throw new InvalidOperationException($"Usage: tiro {recordType}-add <origin> <text>");
    }

    var report = store.AddOperationalRecord(
        recordType,
        string.Join(' ', options.Arguments.Skip(1)),
        options.Arguments[0],
        options.SessionId);
    Console.WriteLine(JsonSerializer.Serialize(report, jsonOptions));
}

static void WriteOperationalList(TiroStore store, string recordType, CliOptions options, JsonSerializerOptions jsonOptions)
{
    Console.WriteLine(JsonSerializer.Serialize(
        store.ListOperationalRecords(recordType, options.SessionId, options.Limit),
        jsonOptions));
}
