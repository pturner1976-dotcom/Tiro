using System.Text.Json.Serialization;

namespace Tiro.Cli;

public sealed record Source(
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("source_path")] string SourcePath,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_utc")] DateTimeOffset CreatedUtc,
    [property: JsonPropertyName("last_ingested_utc")] DateTimeOffset? LastIngestedUtc);

public sealed record Document(
    [property: JsonPropertyName("document_id")] string DocumentId,
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("source_path")] string SourcePath,
    [property: JsonPropertyName("timeframe_or_era")] string TimeframeOrEra,
    [property: JsonPropertyName("created_utc")] DateTimeOffset CreatedUtc);

public sealed record Chunk(
    [property: JsonPropertyName("chunk_id")] string ChunkId,
    [property: JsonPropertyName("document_id")] string DocumentId,
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("source_path")] string SourcePath,
    [property: JsonPropertyName("timeframe_or_era")] string TimeframeOrEra,
    [property: JsonPropertyName("chunk_index")] int ChunkIndex,
    [property: JsonPropertyName("chunk_count")] int ChunkCount,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("ingested_utc")] DateTimeOffset IngestedUtc);

public sealed record TiroRecallProxy(
    [property: JsonPropertyName("proxy_id")] string ProxyId,
    [property: JsonPropertyName("lane")] string Lane,
    [property: JsonPropertyName("proxy_type")] string ProxyType,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("breadcrumb")] string Breadcrumb,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("keywords")] string Keywords,
    [property: JsonPropertyName("entities")] string Entities,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("document_id")] string? DocumentId,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("record_id")] long? RecordId,
    [property: JsonPropertyName("fact_id")] int? FactId,
    [property: JsonPropertyName("pointer_id")] string PointerId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_utc")] DateTimeOffset CreatedUtc,
    [property: JsonPropertyName("updated_utc")] DateTimeOffset UpdatedUtc,
    [property: JsonPropertyName("metadata_json")] string MetadataJson);

public sealed record TiroEvidencePointer(
    [property: JsonPropertyName("pointer_id")] string PointerId,
    [property: JsonPropertyName("target_lane")] string TargetLane,
    [property: JsonPropertyName("target_kind")] string TargetKind,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("document_id")] string? DocumentId,
    [property: JsonPropertyName("chunk_id_start")] string? ChunkIdStart,
    [property: JsonPropertyName("chunk_id_end")] string? ChunkIdEnd,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("message_id_start")] long? MessageIdStart,
    [property: JsonPropertyName("message_id_end")] long? MessageIdEnd,
    [property: JsonPropertyName("record_id")] long? RecordId,
    [property: JsonPropertyName("fact_id")] int? FactId,
    [property: JsonPropertyName("text_hash")] string TextHash,
    [property: JsonPropertyName("created_utc")] DateTimeOffset CreatedUtc,
    [property: JsonPropertyName("metadata_json")] string MetadataJson);

public sealed record TiroHydratedPointerEvidence(
    [property: JsonPropertyName("pointer_id")] string PointerId,
    [property: JsonPropertyName("proxy_id")] string? ProxyId,
    [property: JsonPropertyName("target_lane")] string TargetLane,
    [property: JsonPropertyName("target_kind")] string TargetKind,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("document_id")] string? DocumentId,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("record_id")] long? RecordId,
    [property: JsonPropertyName("fact_id")] int? FactId,
    [property: JsonPropertyName("chunk_ids")] IReadOnlyList<string> ChunkIds,
    [property: JsonPropertyName("message_ids")] IReadOnlyList<long> MessageIds,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("text_hash")] string TextHash,
    [property: JsonPropertyName("timestamp_utc")] DateTimeOffset? TimestampUtc,
    [property: JsonPropertyName("provenance")] string Provenance);

public sealed record TiroProxyBuildResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("db_path")] string DbPath,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("document_id")] string? DocumentId,
    [property: JsonPropertyName("proxies_created")] int ProxiesCreated,
    [property: JsonPropertyName("proxies_superseded")] int ProxiesSuperseded,
    [property: JsonPropertyName("pointers_created")] int PointersCreated,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record TiroProxyInspectResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("db_path")] string DbPath,
    [property: JsonPropertyName("filters")] TiroProxyInspectFilters Filters,
    [property: JsonPropertyName("proxy_count")] int ProxyCount,
    [property: JsonPropertyName("proxies")] IReadOnlyList<TiroRecallProxy> Proxies,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record TiroProxyInspectFilters(
    [property: JsonPropertyName("lane")] string? Lane,
    [property: JsonPropertyName("document_id")] string? DocumentId,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("limit")] int Limit);

public sealed record TiroProxyRecallResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("db_path")] string DbPath,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("normalized_terms")] IReadOnlyList<string> NormalizedTerms,
    [property: JsonPropertyName("proxy_candidate_count")] int ProxyCandidateCount,
    [property: JsonPropertyName("hydrated_evidence_count")] int HydratedEvidenceCount,
    [property: JsonPropertyName("proxies")] IReadOnlyList<TiroRecallProxy> Proxies,
    [property: JsonPropertyName("hydrated_evidence")] IReadOnlyList<TiroHydratedPointerEvidence> HydratedEvidence,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("unknowns")] IReadOnlyList<string> Unknowns,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record Session(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("created_utc")] DateTimeOffset CreatedUtc);

public sealed record Message(
    [property: JsonPropertyName("message_id")] long MessageId,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("timestamp_utc")] DateTimeOffset TimestampUtc,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("source_identity")] string SourceIdentity,
    [property: JsonPropertyName("text")] string Text);

public sealed record FactRecord(
    [property: JsonPropertyName("fact_id")] int FactId,
    [property: JsonPropertyName("created_utc")] DateTimeOffset CreatedUtc,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("origin_identity")] string OriginIdentity,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("linked_source_ids")] IReadOnlyList<string> LinkedSourceIds,
    [property: JsonPropertyName("linked_message_ids")] IReadOnlyList<long> LinkedMessageIds,
    [property: JsonPropertyName("linked_record_ids")] IReadOnlyList<long> LinkedRecordIds,
    [property: JsonPropertyName("supersedes_fact_id")] int? SupersedesFactId,
    [property: JsonPropertyName("superseded_by_fact_id")] int? SupersededByFactId,
    [property: JsonPropertyName("freshness_hint")] string? FreshnessHint,
    [property: JsonPropertyName("expires_utc")] DateTimeOffset? ExpiresUtc,
    [property: JsonPropertyName("archived_utc")] DateTimeOffset? ArchivedUtc = null)
{
    [property: JsonPropertyName("evidence_type")]
    public string EvidenceType { get; init; } = "fact-lifecycle";
}

public sealed record FactConflictRecord(
    [property: JsonPropertyName("fact_id_1")] int FactId1,
    [property: JsonPropertyName("fact_id_2")] int FactId2);

public sealed record RetrievalPolicySummary(
    [property: JsonPropertyName("query_mode")] string QueryMode,
    [property: JsonPropertyName("mode_reason")] string ModeReason,
    [property: JsonPropertyName("competing_modes")] IReadOnlyList<RetrievalModeMatch> CompetingModes,
    [property: JsonPropertyName("signals")] IReadOnlyList<RetrievalPolicySignal> Signals);

public sealed record RetrievalModeMatch(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("matched_terms")] IReadOnlyList<string> MatchedTerms);

public sealed record RetrievalPolicySignal(
    [property: JsonPropertyName("evidence_key")] string EvidenceKey,
    [property: JsonPropertyName("evidence_type")] string EvidenceType,
    [property: JsonPropertyName("evidence_label")] string EvidenceLabel,
    [property: JsonPropertyName("relevance_score")] int RelevanceScore,
    [property: JsonPropertyName("recency_score")] int RecencyScore,
    [property: JsonPropertyName("importance_score")] int ImportanceScore,
    [property: JsonPropertyName("lifecycle_score")] int LifecycleScore,
    [property: JsonPropertyName("mode_score")] int ModeScore,
    [property: JsonPropertyName("final_score")] int FinalScore,
    [property: JsonPropertyName("timestamp_utc")] DateTimeOffset? TimestampUtc,
    [property: JsonPropertyName("lifecycle_state")] string LifecycleState,
    [property: JsonPropertyName("archived")] bool Archived,
    [property: JsonPropertyName("importance_hint")] string ImportanceHint,
    [property: JsonPropertyName("freshness_hint")] string FreshnessHint,
    [property: JsonPropertyName("explanation")] string Explanation);

public sealed record Unknown(
    [property: JsonPropertyName("text")] string Text);

public sealed record WarningRecord(
    [property: JsonPropertyName("text")] string Text);

public sealed record OperationalRecord(
    [property: JsonPropertyName("record_id")] long RecordId,
    [property: JsonPropertyName("record_type")] string RecordType,
    [property: JsonPropertyName("created_utc")] DateTimeOffset CreatedUtc,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("archived_utc")] DateTimeOffset? ArchivedUtc,
    [property: JsonPropertyName("linked_source_ids")] IReadOnlyList<string> LinkedSourceIds,
    [property: JsonPropertyName("linked_message_ids")] IReadOnlyList<long> LinkedMessageIds);

public sealed record OperationalMemoryEvidence(
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("record_id")] long RecordId,
    [property: JsonPropertyName("record_type")] string RecordType,
    [property: JsonPropertyName("created_utc")] DateTimeOffset CreatedUtc,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("archived_utc")] DateTimeOffset? ArchivedUtc,
    [property: JsonPropertyName("linked_source_ids")] IReadOnlyList<string> LinkedSourceIds,
    [property: JsonPropertyName("linked_message_ids")] IReadOnlyList<long> LinkedMessageIds,
    [property: JsonPropertyName("score")] int Score,
    [property: JsonPropertyName("matched_terms")] IReadOnlyList<string> MatchedTerms,
    [property: JsonPropertyName("score_details")] IReadOnlyList<ScoringDetail> ScoreDetails,
    [property: JsonPropertyName("score_summary")] ScoringSummary ScoreSummary,
    [property: JsonPropertyName("explanation")] string Explanation,
    [property: JsonPropertyName("snippet")] string Snippet);

public sealed record OperationalRecordReport(
    [property: JsonPropertyName("record_id")] long RecordId,
    [property: JsonPropertyName("record_type")] string RecordType,
    [property: JsonPropertyName("created_utc")] DateTimeOffset CreatedUtc,
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("status")] string Status);

public sealed record RetrievalFilters(
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("document_id")] string? DocumentId)
{
    [JsonIgnore]
    public bool HasAny => !string.IsNullOrWhiteSpace(SourceId) || !string.IsNullOrWhiteSpace(DocumentId);
}

public sealed record ScoringDetail(
    [property: JsonPropertyName("term")] string Term,
    [property: JsonPropertyName("text_hits")] int TextHits,
    [property: JsonPropertyName("metadata_hits")] int MetadataHits,
    [property: JsonPropertyName("score")] int Score);

public sealed record ScoringSummary(
    [property: JsonPropertyName("term_score")] int TermScore,
    [property: JsonPropertyName("coverage_bonus")] int CoverageBonus,
    [property: JsonPropertyName("phrase_bonus")] int PhraseBonus,
    [property: JsonPropertyName("density_bonus")] int DensityBonus,
    [property: JsonPropertyName("matched_text_hits")] int MatchedTextHits);

public sealed record RetrievalResult(
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("chunk_id")] string ChunkId,
    [property: JsonPropertyName("document_id")] string DocumentId,
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("source_path")] string SourcePath,
    [property: JsonPropertyName("timeframe_or_era")] string TimeframeOrEra,
    [property: JsonPropertyName("chunk_index")] int ChunkIndex,
    [property: JsonPropertyName("chunk_count")] int ChunkCount,
    [property: JsonPropertyName("ingested_utc")] DateTimeOffset IngestedUtc,
    [property: JsonPropertyName("score")] int Score,
    [property: JsonPropertyName("matched_terms")] IReadOnlyList<string> MatchedTerms,
    [property: JsonPropertyName("score_details")] IReadOnlyList<ScoringDetail> ScoreDetails,
    [property: JsonPropertyName("score_summary")] ScoringSummary ScoreSummary,
    [property: JsonPropertyName("explanation")] string Explanation,
    [property: JsonPropertyName("snippet")] string Snippet);

public sealed record SupportingContext(
    [property: JsonPropertyName("primary_chunk_id")] string PrimaryChunkId,
    [property: JsonPropertyName("chunk_id")] string ChunkId,
    [property: JsonPropertyName("document_id")] string DocumentId,
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("source_path")] string SourcePath,
    [property: JsonPropertyName("timeframe_or_era")] string TimeframeOrEra,
    [property: JsonPropertyName("chunk_index")] int ChunkIndex,
    [property: JsonPropertyName("chunk_count")] int ChunkCount,
    [property: JsonPropertyName("relation")] string Relation,
    [property: JsonPropertyName("distance")] int Distance,
    [property: JsonPropertyName("snippet")] string Snippet);

public sealed record SessionMessageEvidence(
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("message_id")] long MessageId,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("timestamp_utc")] DateTimeOffset TimestampUtc,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("source_identity")] string SourceIdentity,
    [property: JsonPropertyName("score")] int Score,
    [property: JsonPropertyName("matched_terms")] IReadOnlyList<string> MatchedTerms,
    [property: JsonPropertyName("score_details")] IReadOnlyList<ScoringDetail> ScoreDetails,
    [property: JsonPropertyName("score_summary")] ScoringSummary ScoreSummary,
    [property: JsonPropertyName("explanation")] string Explanation,
    [property: JsonPropertyName("snippet")] string Snippet);

public sealed record PlannerMetadata(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("key_name")] string KeyName,
    [property: JsonPropertyName("key_found")] bool KeyFound,
    [property: JsonPropertyName("env_file_found")] bool EnvFileFound,
    [property: JsonPropertyName("retrieval_query")] string RetrievalQuery,
    [property: JsonPropertyName("refined_terms")] IReadOnlyList<string> RefinedTerms,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("debug")] IReadOnlyList<string> Debug)
{
    [property: JsonPropertyName("semantic_intent")]
    public string SemanticIntent { get; init; } = "unclear";

    [property: JsonPropertyName("expanded_queries")]
    public IReadOnlyList<string> ExpandedQueries { get; init; } = Array.Empty<string>();

    [property: JsonPropertyName("expanded_terms")]
    public IReadOnlyList<string> ExpandedTerms { get; init; } = Array.Empty<string>();

    [property: JsonPropertyName("target_lanes")]
    public IReadOnlyList<string> TargetLanes { get; init; } = Array.Empty<string>();

    [property: JsonPropertyName("required_entities")]
    public IReadOnlyList<string> RequiredEntities { get; init; } = Array.Empty<string>();

    [property: JsonPropertyName("optional_entities")]
    public IReadOnlyList<string> OptionalEntities { get; init; } = Array.Empty<string>();

    [property: JsonPropertyName("likely_session_scope")]
    public string? LikelySessionScope { get; init; }

    [property: JsonPropertyName("retrieval_strategy")]
    public string RetrievalStrategy { get; init; } = "single_query";

    [property: JsonPropertyName("planner_confidence")]
    public string PlannerConfidence { get; init; } = "low";

    [property: JsonPropertyName("planner_warnings")]
    public IReadOnlyList<string> PlannerWarnings { get; init; } = Array.Empty<string>();

    [property: JsonPropertyName("expanded_query_diagnostics")]
    public IReadOnlyList<ExpandedQueryDiagnostic> ExpandedQueryDiagnostics { get; init; } = Array.Empty<ExpandedQueryDiagnostic>();
}

public sealed record ExpandedQueryDiagnostic(
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("corpus_candidates")] int CorpusCandidates,
    [property: JsonPropertyName("session_candidates")] int SessionCandidates,
    [property: JsonPropertyName("operational_candidates")] int OperationalCandidates,
    [property: JsonPropertyName("lifecycle_candidates")] int LifecycleCandidates);

public sealed record ContextPacket(
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("normalized_terms")] IReadOnlyList<string> NormalizedTerms,
    [property: JsonPropertyName("confidence")] string Confidence,
    [property: JsonPropertyName("facts")] IReadOnlyList<FactRecord> Facts,
    [property: JsonPropertyName("unknowns")] IReadOnlyList<Unknown> Unknowns,
    [property: JsonPropertyName("warnings")] IReadOnlyList<WarningRecord> Warnings,
    [property: JsonPropertyName("sources")] IReadOnlyList<Source> Sources,
    [property: JsonPropertyName("source_ids")] IReadOnlyList<string> SourceIds,
    [property: JsonPropertyName("filters")] RetrievalFilters Filters,
    [property: JsonPropertyName("context_window")] int ContextWindow,
    [property: JsonPropertyName("primary_evidence")] IReadOnlyList<RetrievalResult> PrimaryEvidence,
    [property: JsonPropertyName("supporting_context")] IReadOnlyList<SupportingContext> SupportingContext,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("session_evidence")] IReadOnlyList<SessionMessageEvidence> SessionEvidence,
    [property: JsonPropertyName("recent_session_context")] IReadOnlyList<Message> RecentSessionContext,
    [property: JsonPropertyName("operational_memory")] IReadOnlyList<OperationalMemoryEvidence> OperationalMemory,
    [property: JsonPropertyName("retrieval_policy")] RetrievalPolicySummary RetrievalPolicy,
    [property: JsonPropertyName("retrieval_results")] IReadOnlyList<RetrievalResult> RetrievalResults,
    [property: JsonPropertyName("planner")] PlannerMetadata Planner);

public sealed record SessionCreateReport(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("created")] bool Created,
    [property: JsonPropertyName("created_utc")] DateTimeOffset CreatedUtc);

public sealed record MessageIngestReport(
    [property: JsonPropertyName("message_id")] long MessageId,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("timestamp_utc")] DateTimeOffset TimestampUtc,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("source_identity")] string SourceIdentity);

public sealed record IngestionReport(
    string InputPath,
    int LinesRead,
    int ChunksInserted,
    int DuplicateChunks,
    int SourcesRegistered,
    int DocumentsRegistered,
    DateTimeOffset IngestedUtc);

public sealed record StoreStats(
    int SourceCount,
    int DocumentCount,
    int ChunkCount,
    int SessionCount,
    int MessageCount,
    int OperationalRecordCount,
    int FactCount,
    int FactConflictCount,
    int TotalChunkCharacters);

public sealed record TiroInitRecordCounts(
    [property: JsonPropertyName("corpus_records")] int CorpusRecords,
    [property: JsonPropertyName("session_records")] int SessionRecords,
    [property: JsonPropertyName("operational_records")] int OperationalRecords,
    [property: JsonPropertyName("lifecycle_facts")] int LifecycleFacts,
    [property: JsonPropertyName("recall_proxies")] int RecallProxies,
    [property: JsonPropertyName("evidence_pointers")] int EvidencePointers);

public sealed record TiroInitResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("database_path")] string DatabasePath,
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("product_version")] string ProductVersion,
    [property: JsonPropertyName("created")] bool Created,
    [property: JsonPropertyName("tables")] IReadOnlyList<string> Tables,
    [property: JsonPropertyName("indexes")] IReadOnlyList<string> Indexes,
    [property: JsonPropertyName("record_counts")] TiroInitRecordCounts RecordCounts);

public sealed record TiroSearchDebugRequest(
    [property: JsonPropertyName("database_path")] string DatabasePath,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("limit")] int Limit = 5,
    [property: JsonPropertyName("source_id")] string? SourceId = null,
    [property: JsonPropertyName("document_id")] string? DocumentId = null,
    [property: JsonPropertyName("session_id")] string? SessionId = null,
    [property: JsonPropertyName("planner_mode")] PlannerMode PlannerMode = PlannerMode.Auto,
    [property: JsonPropertyName("debug_planner")] bool DebugPlanner = false,
    [property: JsonPropertyName("include_archived")] bool IncludeArchived = false);

public sealed record TiroSearchDiagnostics(
    [property: JsonPropertyName("database_path")] string DatabasePath,
    [property: JsonPropertyName("original_query")] string OriginalQuery,
    [property: JsonPropertyName("normalized_query_terms")] IReadOnlyList<string> NormalizedQueryTerms,
    [property: JsonPropertyName("query_mode")] string QueryMode,
    [property: JsonPropertyName("mode_reason")] string ModeReason,
    [property: JsonPropertyName("competing_modes")] IReadOnlyList<RetrievalModeMatch> CompetingModes,
    [property: JsonPropertyName("planner_mode")] string PlannerMode,
    [property: JsonPropertyName("planner_status")] string PlannerStatus,
    [property: JsonPropertyName("semantic_intent")] string SemanticIntent,
    [property: JsonPropertyName("planner_query")] string PlannerQuery,
    [property: JsonPropertyName("expanded_queries")] IReadOnlyList<string> ExpandedQueries,
    [property: JsonPropertyName("expanded_terms")] IReadOnlyList<string> ExpandedTerms,
    [property: JsonPropertyName("target_lanes")] IReadOnlyList<string> TargetLanes,
    [property: JsonPropertyName("retrieval_strategy")] string RetrievalStrategy,
    [property: JsonPropertyName("planner_confidence")] string PlannerConfidence,
    [property: JsonPropertyName("planner_warnings")] IReadOnlyList<string> PlannerWarnings,
    [property: JsonPropertyName("expanded_query_diagnostics")] IReadOnlyList<ExpandedQueryDiagnostic> ExpandedQueryDiagnostics,
    [property: JsonPropertyName("source_id_filter")] string? SourceIdFilter,
    [property: JsonPropertyName("document_id_filter")] string? DocumentIdFilter,
    [property: JsonPropertyName("session_id_filter")] string? SessionIdFilter,
    [property: JsonPropertyName("lanes_searched")] IReadOnlyList<string> LanesSearched,
    [property: JsonPropertyName("candidate_counts")] TiroSearchCandidateCounts CandidateCounts,
    [property: JsonPropertyName("matched_terms")] TiroSearchMatchedTerms MatchedTerms,
    [property: JsonPropertyName("top_result_score_components")] IReadOnlyList<TiroSearchTopScore> TopResultScoreComponents,
    [property: JsonPropertyName("database_counts")] StoreStats DatabaseCounts,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("unknowns")] IReadOnlyList<string> Unknowns);

public sealed record TiroSearchCandidateCounts(
    [property: JsonPropertyName("corpus")] int Corpus,
    [property: JsonPropertyName("session")] int Session,
    [property: JsonPropertyName("operational")] int Operational,
    [property: JsonPropertyName("lifecycle")] int Lifecycle);

public sealed record TiroSearchMatchedTerms(
    [property: JsonPropertyName("corpus")] IReadOnlyList<string> Corpus,
    [property: JsonPropertyName("session")] IReadOnlyList<string> Session,
    [property: JsonPropertyName("operational")] IReadOnlyList<string> Operational,
    [property: JsonPropertyName("lifecycle")] IReadOnlyList<string> Lifecycle);

public sealed record TiroSearchTopScore(
    [property: JsonPropertyName("lane")] string Lane,
    [property: JsonPropertyName("score")] int Score,
    [property: JsonPropertyName("matched_terms")] IReadOnlyList<string> MatchedTerms,
    [property: JsonPropertyName("summary")] ScoringSummary? Summary,
    [property: JsonPropertyName("retrieval_policy_signal")] RetrievalPolicySignal? RetrievalPolicySignal);

public sealed record TiroProofCarryForwardResult(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("database_path")] string DatabasePath,
    [property: JsonPropertyName("phrase")] string Phrase,
    [property: JsonPropertyName("record_type")] string RecordType,
    [property: JsonPropertyName("before_count")] int BeforeCount,
    [property: JsonPropertyName("after_count")] int AfterCount,
    [property: JsonPropertyName("fresh_process_count")] int FreshProcessCount,
    [property: JsonPropertyName("record_id")] long RecordId,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);

public sealed record TiroInspectSessionsResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("db_path")] string DbPath,
    [property: JsonPropertyName("session_count")] int SessionCount,
    [property: JsonPropertyName("sessions")] IReadOnlyList<TiroSessionInventoryItem> Sessions,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record TiroSessionInventoryItem(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("message_count")] int MessageCount,
    [property: JsonPropertyName("first_timestamp_utc")] DateTimeOffset? FirstTimestampUtc,
    [property: JsonPropertyName("last_timestamp_utc")] DateTimeOffset? LastTimestampUtc,
    [property: JsonPropertyName("source_identities")] IReadOnlyList<string> SourceIdentities);

public sealed record TiroInspectOperationalResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("db_path")] string DbPath,
    [property: JsonPropertyName("filters")] TiroOperationalInspectFilters Filters,
    [property: JsonPropertyName("record_count")] int RecordCount,
    [property: JsonPropertyName("records")] IReadOnlyList<TiroOperationalInspectItem> Records,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record TiroOperationalInspectFilters(
    [property: JsonPropertyName("record_type")] string? RecordType,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("limit")] int Limit);

public sealed record TiroOperationalInspectItem(
    [property: JsonPropertyName("record_id")] long RecordId,
    [property: JsonPropertyName("record_type")] string RecordType,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("source_identity")] string SourceIdentity,
    [property: JsonPropertyName("created_utc")] DateTimeOffset CreatedUtc,
    [property: JsonPropertyName("text")] string Text);

public sealed record TiroInspectRecentResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("db_path")] string DbPath,
    [property: JsonPropertyName("recent_session_messages")] IReadOnlyList<Message> RecentSessionMessages,
    [property: JsonPropertyName("recent_operational_records")] IReadOnlyList<TiroOperationalInspectItem> RecentOperationalRecords,
    [property: JsonPropertyName("recent_facts")] IReadOnlyList<FactRecord> RecentFacts,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record TiroInspectStatsResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("db_path")] string DbPath,
    [property: JsonPropertyName("stats")] StoreStats Stats,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record TiroSourceInventoryItem(
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("source_path")] string SourcePath,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_utc")] DateTimeOffset CreatedUtc,
    [property: JsonPropertyName("last_ingested_utc")] DateTimeOffset? LastIngestedUtc,
    [property: JsonPropertyName("document_count")] int DocumentCount,
    [property: JsonPropertyName("chunk_count")] int ChunkCount);

public sealed record TiroDocumentInventoryItem(
    [property: JsonPropertyName("document_id")] string DocumentId,
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("source_path")] string SourcePath,
    [property: JsonPropertyName("timeframe_or_era")] string TimeframeOrEra,
    [property: JsonPropertyName("created_utc")] DateTimeOffset CreatedUtc,
    [property: JsonPropertyName("chunk_count")] int ChunkCount,
    [property: JsonPropertyName("first_chunk_id")] string? FirstChunkId,
    [property: JsonPropertyName("last_chunk_id")] string? LastChunkId);

public sealed record TiroInspectSourcesResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("db_path")] string DbPath,
    [property: JsonPropertyName("source_count")] int SourceCount,
    [property: JsonPropertyName("sources")] IReadOnlyList<TiroSourceInventoryItem> Sources,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record TiroInspectDocumentsResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("db_path")] string DbPath,
    [property: JsonPropertyName("filters")] TiroDocumentInspectFilters Filters,
    [property: JsonPropertyName("document_count")] int DocumentCount,
    [property: JsonPropertyName("documents")] IReadOnlyList<TiroDocumentInventoryItem> Documents,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record TiroDocumentInspectFilters(
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("limit")] int Limit);

public sealed record TiroSessionSummaryResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("db_path")] string DbPath,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("message_count_total")] int MessageCountTotal,
    [property: JsonPropertyName("message_count_returned")] int MessageCountReturned,
    [property: JsonPropertyName("first_timestamp_utc")] DateTimeOffset? FirstTimestampUtc,
    [property: JsonPropertyName("last_timestamp_utc")] DateTimeOffset? LastTimestampUtc,
    [property: JsonPropertyName("messages")] IReadOnlyList<Message> Messages,
    [property: JsonPropertyName("summary_basis")] string SummaryBasis,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record TiroPhraseSearchResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("db_path")] string DbPath,
    [property: JsonPropertyName("phrase")] string Phrase,
    [property: JsonPropertyName("lane")] string Lane,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("result_count")] int ResultCount,
    [property: JsonPropertyName("results")] IReadOnlyList<TiroPhraseSearchResult> Results,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record TiroPhraseSearchResult(
    [property: JsonPropertyName("lane")] string Lane,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("source_identity")] string? SourceIdentity,
    [property: JsonPropertyName("timestamp_utc")] DateTimeOffset? TimestampUtc,
    [property: JsonPropertyName("text_snippet")] string TextSnippet);

public sealed record AichatSessionCandidate(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("modified_utc")] DateTimeOffset ModifiedUtc,
    [property: JsonPropertyName("derived_session_id")] string DerivedSessionId);

public sealed record AichatSessionInspection(
    [property: JsonPropertyName("sessions_dir")] string SessionsDir,
    [property: JsonPropertyName("candidate_count")] int CandidateCount,
    [property: JsonPropertyName("newest_file")] string? NewestFile,
    [property: JsonPropertyName("candidates")] IReadOnlyList<AichatSessionCandidate> Candidates,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);

public sealed record TiroSessionSearchResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("db_path")] string DbPath,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("normalized_terms")] IReadOnlyList<string> NormalizedTerms,
    [property: JsonPropertyName("session_limit")] int SessionLimit,
    [property: JsonPropertyName("result_count")] int ResultCount,
    [property: JsonPropertyName("sessions")] IReadOnlyList<TiroSessionSearchGroup> Sessions,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record TiroSessionSearchGroup(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("message_count_total")] int MessageCountTotal,
    [property: JsonPropertyName("first_timestamp_utc")] DateTimeOffset? FirstTimestampUtc,
    [property: JsonPropertyName("last_timestamp_utc")] DateTimeOffset? LastTimestampUtc,
    [property: JsonPropertyName("match_count")] int MatchCount,
    [property: JsonPropertyName("matches")] IReadOnlyList<SessionMessageEvidence> Matches);

public sealed record TiroRecallRequest(
    [property: JsonPropertyName("database_path")] string DatabasePath,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("limit")] int Limit = 5,
    [property: JsonPropertyName("planner_mode")] PlannerMode PlannerMode = PlannerMode.Auto,
    [property: JsonPropertyName("debug_planner")] bool DebugPlanner = false,
    [property: JsonPropertyName("session_limit")] int SessionLimit = 10,
    [property: JsonPropertyName("source_limit")] int SourceLimit = 20,
    [property: JsonPropertyName("document_limit")] int DocumentLimit = 20,
    [property: JsonPropertyName("include_archived")] bool IncludeArchived = false);

public sealed record TiroProxyBuildRequest(
    [property: JsonPropertyName("database_path")] string DatabasePath,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("document_id")] string? DocumentId,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("rebuild")] bool Rebuild);

public sealed record TiroProxyRecallRequest(
    [property: JsonPropertyName("database_path")] string DatabasePath,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("limit")] int Limit = 5);

public sealed record TiroRecallResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("db_path")] string DbPath,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("interpreted_intent")] string InterpretedIntent,
    [property: JsonPropertyName("searched_lanes")] IReadOnlyList<string> SearchedLanes,
    [property: JsonPropertyName("inspected_scopes")] IReadOnlyList<string> InspectedScopes,
    [property: JsonPropertyName("candidate_counts")] TiroSearchCandidateCounts CandidateCounts,
    [property: JsonPropertyName("likely_sessions")] IReadOnlyList<TiroSessionSearchGroup> LikelySessions,
    [property: JsonPropertyName("likely_sources")] IReadOnlyList<TiroSourceInventoryItem> LikelySources,
    [property: JsonPropertyName("likely_documents")] IReadOnlyList<TiroDocumentInventoryItem> LikelyDocuments,
    [property: JsonPropertyName("evidence")] IReadOnlyList<TiroRecallEvidenceItem> Evidence,
    [property: JsonPropertyName("proxy_candidates")] IReadOnlyList<TiroRecallProxy> ProxyCandidates,
    [property: JsonPropertyName("proxy_hydrated_evidence")] IReadOnlyList<TiroHydratedPointerEvidence> ProxyHydratedEvidence,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("unknowns")] IReadOnlyList<string> Unknowns,
    [property: JsonPropertyName("planner")] PlannerMetadata Planner);

public sealed record TiroRecallEvidenceItem(
    [property: JsonPropertyName("lane")] string Lane,
    [property: JsonPropertyName("evidence_id")] string EvidenceId,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("document_id")] string? DocumentId,
    [property: JsonPropertyName("record_type")] string? RecordType,
    [property: JsonPropertyName("timestamp_utc")] DateTimeOffset? TimestampUtc,
    [property: JsonPropertyName("archived")] bool Archived,
    [property: JsonPropertyName("archived_utc")] DateTimeOffset? ArchivedUtc,
    [property: JsonPropertyName("score")] int Score,
    [property: JsonPropertyName("matched_terms")] IReadOnlyList<string> MatchedTerms,
    [property: JsonPropertyName("snippet")] string Snippet,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("proxy_id")] string? ProxyId = null,
    [property: JsonPropertyName("pointer_id")] string? PointerId = null,
    [property: JsonPropertyName("proxy_title")] string? ProxyTitle = null,
    [property: JsonPropertyName("proxy_breadcrumb")] string? ProxyBreadcrumb = null);

public sealed record TiroArchiveCandidate(
    [property: JsonPropertyName("evidence_key")] string EvidenceKey,
    [property: JsonPropertyName("lane")] string Lane,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_utc")] DateTimeOffset CreatedUtc,
    [property: JsonPropertyName("archived_utc")] DateTimeOffset? ArchivedUtc,
    [property: JsonPropertyName("text_snippet")] string TextSnippet);

public sealed record TiroArchiveResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("older_than_days")] int OlderThanDays,
    [property: JsonPropertyName("dry_run")] bool DryRun,
    [property: JsonPropertyName("status_override")] string? StatusOverride,
    [property: JsonPropertyName("archived_count")] int ArchivedCount,
    [property: JsonPropertyName("candidates")] IReadOnlyList<TiroArchiveCandidate> Candidates,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);

public sealed record TiroUnarchiveResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("evidence_key")] string? EvidenceKey,
    [property: JsonPropertyName("all_since")] DateTimeOffset? AllSince,
    [property: JsonPropertyName("unarchived_count")] int UnarchivedCount,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);
