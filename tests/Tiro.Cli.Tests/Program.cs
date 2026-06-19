using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Tiro.Cli;

Run("planner disabled fallback", async () =>
{
    var config = Config(PlannerMode.Off, apiKey: null, keyFound: false);
    var planner = new RetrievalPlanner(config, new FakePlannerClient(PlannerAdvice.Empty("ignored")));
    var result = await planner.PlanAsync("provenance retrieval", debugPlanner: false, CancellationToken.None);
    AssertEqual("disabled", result.Status);
    AssertEqual("provenance retrieval", result.RetrievalQuery);
});

Run("planner unavailable fallback", async () =>
{
    var config = Config(PlannerMode.Auto, apiKey: null, keyFound: false);
    var planner = new RetrievalPlanner(config, new FakePlannerClient(PlannerAdvice.Empty("ignored")));
    var result = await planner.PlanAsync("provenance retrieval", debugPlanner: false, CancellationToken.None);
    AssertEqual("unavailable", result.Status);
    AssertEqual("provenance retrieval", result.RetrievalQuery);
});

Run("planner failure fallback", async () =>
{
    var config = Config(PlannerMode.On, apiKey: "test-key", keyFound: true);
    var planner = new RetrievalPlanner(config, new FailingPlannerClient());
    var result = await planner.PlanAsync("provenance retrieval", debugPlanner: true, CancellationToken.None);
    AssertEqual("failed", result.Status);
    AssertEqual("provenance retrieval", result.RetrievalQuery);
    AssertContains(result.Debug, "gemini_endpoint_url=");
    AssertContains(result.Debug, "gemini_auth_method=query_parameter");
});

Run("planner present query refinement", async () =>
{
    var advice = new PlannerAdvice("source provenance retrieval", new[] { "source", "provenance", "retrieval" }, new[] { "source" });
    var config = Config(PlannerMode.On, apiKey: "test-key", keyFound: true);
    var planner = new RetrievalPlanner(config, new FakePlannerClient(advice));
    var result = await planner.PlanAsync("where is evidence kept", debugPlanner: true, CancellationToken.None);
    AssertEqual("used", result.Status);
    AssertEqual("source provenance retrieval", result.RetrievalQuery);
    AssertEqual(3, result.RefinedTerms.Count);
    AssertContains(result.Debug, "gemini_endpoint_url=");
    AssertContains(result.Debug, "gemini_timeout_ms=500");
});

Run("planner advice json parsing", () =>
{
    var advice = PlannerAdvice.Parse(
        """
        {"search_query":"  provenance   retrieval  ","refined_terms":["sources","warnings"],"packet_focus":["facts"]}
        """);
    AssertEqual("provenance retrieval", advice.SearchQuery);
    AssertEqual("source", advice.RefinedTerms[0]);
    AssertEqual("warning", advice.RefinedTerms[1]);
    return Task.CompletedTask;
});

Run("planner advice wrapped json parsing", () =>
{
    var fenced = PlannerAdvice.Parse(
        """
        ```json
        {"search_query":"context packets","refined_terms":["context","packets"],"packet_focus":["sources"],"warnings":["partial evidence only"]}
        ```
        """);
    AssertEqual("context packets", fenced.SearchQuery);
    AssertEqual("source", fenced.PacketFocus[0]);
    AssertEqual("partial evidence only", fenced.Warnings[0]);

    var preamble = PlannerAdvice.Parse(
        """
        Here is the planner JSON:
        {"search_query":"provenance retrieval","refined_terms":["provenance","retrieval"],"packet_focus":["facts"]}
        """);
    AssertEqual("provenance retrieval", preamble.SearchQuery);
    return Task.CompletedTask;
});

Run("semantic planner advice parses and caps expansion", () =>
{
    var advice = PlannerAdvice.Parse(
        """
        {
          "search_query":"session memory checkpoint",
          "refined_terms":["session","memory"],
          "packet_focus":["session"],
          "semantic_intent":"session_recall",
          "expanded_queries":["session evidence saved sessions checkpointing","saved chat transcript ingestion","conversation memory","checkpoint recall","session state","ignored sixth"],
          "expanded_terms":["session","evidence","saved","checkpoint","ingestion","transcript","conversation","memory","state","one","two","three","four","five","six","seven","eight","nine","ten","eleven","twelve","thirteen","fourteen","fifteen","sixteen","seventeen","eighteen","nineteen","twenty","twentyone","twentytwo","twentythree","twentyfour","twentyfive","twentysix"],
          "target_lanes":["session","operational","bad_lane"],
          "required_entities":["session_new"],
          "optional_entities":["checkpointing"],
          "likely_session_scope":"session_new",
          "retrieval_strategy":"multi_query_union",
          "confidence":"medium",
          "planner_warnings":["broad session recall"]
        }
        """);
    AssertEqual("session_recall", advice.SemanticIntent);
    AssertEqual(5, advice.ExpandedQueries.Count);
    AssertEqual(25, advice.ExpandedTerms.Count);
    AssertEqual(true, advice.TargetLanes.Contains("session"));
    AssertEqual(false, advice.TargetLanes.Contains("bad_lane"));
    AssertEqual("multi_query_union", advice.RetrievalStrategy);
    AssertEqual("medium", advice.Confidence);
    return Task.CompletedTask;
});

Run("semantic planner cannot omit required fields", () =>
{
    AssertThrows(() => PlannerAdvice.Parse(
        """
        {"search_query":"x","refined_terms":[],"packet_focus":[],"semantic_intent":"topic_search"}
        """));
    return Task.CompletedTask;
});

Run("planner advice truncated output rejected", () =>
{
    AssertThrows(() => PlannerAdvice.Parse("""{"search_query":"provenance","refined_terms":["source"],"packet_focus":["facts"]"""));
    return Task.CompletedTask;
});

Run("planner advice invalid output rejected", () =>
{
    AssertThrows(() => PlannerAdvice.Parse("Sure, I can help with that."));
    AssertThrows(() => PlannerAdvice.Parse("""noise {"search_query":"x","refined_terms":[],"packet_focus":[]} trailing"""));
    AssertThrows(() => PlannerAdvice.Parse("""{"search_query":"x","refined_terms":[]}"""));
    return Task.CompletedTask;
});

Run("planner invalid output falls back", async () =>
{
    var config = Config(PlannerMode.On, apiKey: "test-key", keyFound: true);
    var planner = new RetrievalPlanner(config, new InvalidOutputPlannerClient());
    var result = await planner.PlanAsync("provenance retrieval", debugPlanner: true, CancellationToken.None);
    AssertEqual("failed", result.Status);
    AssertEqual("provenance retrieval", result.RetrievalQuery);
});

Run("planner default model is current", () =>
{
    var previousHome = Environment.GetEnvironmentVariable("HOME");
    var previousModel = Environment.GetEnvironmentVariable("GEMINI_PLANNER_MODEL");
    try
    {
        Environment.SetEnvironmentVariable("HOME", "/tmp/tiro_test_no_env");
        Environment.SetEnvironmentVariable("GEMINI_PLANNER_MODEL", null);
        var config = PlannerConfig.Load(PlannerMode.Auto);
        AssertEqual("gemini-2.5-flash", config.Model);
        return Task.CompletedTask;
    }
    finally
    {
        Environment.SetEnvironmentVariable("HOME", previousHome);
        Environment.SetEnvironmentVariable("GEMINI_PLANNER_MODEL", previousModel);
    }
});

Run("source filtering narrows deterministic retrieval", () =>
{
    using var store = BuildFixtureStore("source_filter");
    var planner = PlannerRunResult.Disabled(Config(PlannerMode.Off, null, false), "provenance retrieval");
    var packet = new ContextPacketBuilder(store).Build(
        "provenance retrieval",
        5,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters("source:m003-quality-notes-txt-fixture-m003-quality-notes-txt", null),
        0);
    AssertEqual(true, packet.PrimaryEvidence.Count > 0);
    AssertEqual(true, packet.PrimaryEvidence.All(result => result.SourceId == "source:m003-quality-notes-txt-fixture-m003-quality-notes-txt"));
    AssertContains(packet.Warnings.Select(warning => warning.Text), "Explicit retrieval filters applied");
    return Task.CompletedTask;
});

Run("document filtering narrows deterministic retrieval", () =>
{
    using var store = BuildFixtureStore("document_filter");
    var planner = PlannerRunResult.Disabled(Config(PlannerMode.Off, null, false), "packets confidence");
    var packet = new ContextPacketBuilder(store).Build(
        "packets confidence",
        5,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, "m003_quality"),
        0);
    AssertEqual(true, packet.PrimaryEvidence.Count > 0);
    AssertEqual(true, packet.PrimaryEvidence.All(result => result.DocumentId == "m003_quality"));
    return Task.CompletedTask;
});

Run("filter miss is not broadened", () =>
{
    using var store = BuildFixtureStore("filter_miss");
    var planner = PlannerRunResult.Disabled(Config(PlannerMode.Off, null, false), "provenance retrieval");
    var packet = new ContextPacketBuilder(store).Build(
        "provenance retrieval",
        5,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters("source:not-present", null),
        1);
    AssertEqual(0, packet.PrimaryEvidence.Count);
    AssertEqual(0, packet.SupportingContext.Count);
    AssertContains(packet.Unknowns.Select(unknown => unknown.Text), "No stored chunks matched the requested source/document filters.");
    AssertContains(packet.Warnings.Select(warning => warning.Text), "not silently broadened");
    return Task.CompletedTask;
});

Run("adjacent context window preserves provenance", () =>
{
    using var store = BuildFixtureStore("context_window");
    var planner = PlannerRunResult.Disabled(Config(PlannerMode.Off, null, false), "packets confidence warnings sources");
    var packet = new ContextPacketBuilder(store).Build(
        "packets confidence warnings sources",
        1,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, "m003_quality"),
        1);
    AssertEqual(1, packet.PrimaryEvidence.Count);
    AssertEqual(true, packet.SupportingContext.Count > 0);
    AssertEqual(packet.PrimaryEvidence[0].ChunkId, packet.SupportingContext[0].PrimaryChunkId);
    AssertEqual("m003_quality", packet.SupportingContext[0].DocumentId);
    AssertEqual(true, packet.SupportingContext.All(context => context.Distance <= 1));
    return Task.CompletedTask;
});

Run("session creation and message ingestion are deterministic", () =>
{
    using var store = BuildFixtureStore("session_ingest");
    var created = store.CreateSession("session-alpha", DateTimeOffset.Parse("2026-05-10T00:00:00Z"));
    var existing = store.CreateSession("session-alpha", DateTimeOffset.Parse("2026-05-10T00:01:00Z"));
    AssertEqual(true, created.Created);
    AssertEqual(false, existing.Created);
    AssertEqual(DateTimeOffset.Parse("2026-05-10T00:00:00Z"), existing.CreatedUtc);

    var report = store.IngestMessage(
        "session-alpha",
        "user",
        "cli:test",
        "The current project decision is to keep provenance explicit.",
        DateTimeOffset.Parse("2026-05-10T00:02:00Z"));
    AssertEqual("session-alpha", report.SessionId);
    AssertEqual("user", report.Direction);
    AssertEqual("cli:test", report.SourceIdentity);
    return Task.CompletedTask;
});

Run("recent session retrieval is chronological and bounded", () =>
{
    using var store = BuildSessionStore("session_recent");
    var recent = store.GetRecentMessages("session-alpha", 2);
    AssertEqual(2, recent.Count);
    AssertEqual("assistant", recent[0].Direction);
    AssertEqual("user", recent[1].Direction);
    AssertEqual(true, recent[0].TimestampUtc < recent[1].TimestampUtc);
    return Task.CompletedTask;
});

Run("session query returns provenance-aware evidence", () =>
{
    using var store = BuildSessionStore("session_query");
    var evidence = store.QuerySessionMessages("session-alpha", "deployment token rotation", 3);
    AssertEqual(true, evidence.Count > 0);
    AssertEqual("session-alpha", evidence[0].SessionId);
    AssertEqual("cli:user", evidence[0].SourceIdentity);
    AssertEqual(true, evidence[0].MatchedTerms.Contains("deployment"));
    return Task.CompletedTask;
});

Run("packet combines corpus and session evidence distinctly", () =>
{
    using var store = BuildSessionStore("mixed_packet");
    var planner = PlannerRunResult.Disabled(Config(PlannerMode.Off, null, false), "provenance deployment token");
    var packet = new ContextPacketBuilder(store).Build(
        "provenance deployment token",
        3,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, null),
        0,
        "session-alpha");
    AssertEqual("session-alpha", packet.SessionId);
    AssertEqual(true, packet.PrimaryEvidence.Count > 0);
    AssertEqual(true, packet.SessionEvidence.Count > 0);
    AssertEqual(true, packet.Facts.Any(fact => fact.EvidenceType == "corpus"));
    AssertEqual(true, packet.Facts.Any(fact => fact.EvidenceType == "session"));
    AssertContains(packet.Warnings.Select(warning => warning.Text), "Session/state evidence lane enabled");
    return Task.CompletedTask;
});

Run("operational memory creation and listing works", () =>
{
    using var store = BuildSessionStore("operational_create");
    var decision = store.AddOperationalRecord("decision", "Use explicit provenance for release notes.", "test:decision", "session-alpha", createdUtc: DateTimeOffset.Parse("2026-05-10T00:04:00Z"));
    var todo = store.AddOperationalRecord("todo", "Rotate deployment token before launch.", "test:todo", "session-alpha", createdUtc: DateTimeOffset.Parse("2026-05-10T00:05:00Z"));
    var unknown = store.AddOperationalRecord("unknown", "Deployment owner is not confirmed.", "test:unknown", "session-alpha", createdUtc: DateTimeOffset.Parse("2026-05-10T00:06:00Z"));
    var warning = store.AddOperationalRecord("warning", "Token rotation is blocked until owner confirms.", "test:warning", "session-alpha", createdUtc: DateTimeOffset.Parse("2026-05-10T00:07:00Z"));

    AssertEqual("decision", decision.RecordType);
    AssertEqual("todo", todo.RecordType);
    AssertEqual("unknown", unknown.RecordType);
    AssertEqual("warning", warning.RecordType);
    AssertEqual(1, store.ListOperationalRecords("decision", "session-alpha", 10).Count);
    AssertEqual(1, store.ListOperationalRecords("todo", "session-alpha", 10).Count);
    AssertEqual(1, store.ListOperationalRecords("unknown", "session-alpha", 10).Count);
    AssertEqual(1, store.ListOperationalRecords("warning", "session-alpha", 10).Count);
    return Task.CompletedTask;
});

Run("operational memory retrieval is deterministic and scoped", () =>
{
    using var store = BuildOperationalStore("operational_query");
    var scoped = store.QueryOperationalRecords("deployment token owner", 5, "session-alpha");
    var unscoped = store.QueryOperationalRecords("global release policy", 5, "session-alpha");
    AssertEqual(true, scoped.Count >= 2);
    AssertEqual(true, scoped.All(record => record.SessionId == "session-alpha"));
    AssertEqual(0, unscoped.Count);
    AssertEqual(true, scoped.Any(record => record.RecordType == "todo"));
    AssertEqual(true, scoped.Any(record => record.RecordType == "warning"));
    return Task.CompletedTask;
});

Run("packet includes operational memory distinctly", () =>
{
    using var store = BuildOperationalStore("operational_packet");
    var planner = PlannerRunResult.Disabled(Config(PlannerMode.Off, null, false), "deployment token owner");
    var packet = new ContextPacketBuilder(store).Build(
        "deployment token owner",
        4,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, null),
        0,
        "session-alpha");
    AssertEqual(true, packet.OperationalMemory.Count > 0);
    AssertEqual(true, packet.OperationalMemory.Any(record => record.RecordType == "decision"));
    AssertEqual(true, packet.OperationalMemory.Any(record => record.RecordType == "todo"));
    AssertEqual(true, packet.Unknowns.Any(unknown => unknown.Text.Contains("Operational unknown", StringComparison.Ordinal)));
    AssertEqual(true, packet.Warnings.Any(warning => warning.Text.Contains("Operational warning", StringComparison.Ordinal)));
    AssertEqual(true, packet.Facts.Any(fact => fact.EvidenceType == "operational"));
    return Task.CompletedTask;
});

Run("fact lifecycle statuses are explicit and listable", () =>
{
    using var store = BuildFixtureStore("fact_statuses");
    foreach (var status in TiroStore.FactStatus.AllStatuses)
    {
        var fact = store.AddFact(
            $"Lifecycle status {status} verification fact",
            $"source:fact-status-{status}",
            "test:lifecycle",
            status,
            createdUtc: DateTimeOffset.Parse("2026-05-10T00:10:00Z"));
        AssertEqual(status, fact.Status);
        AssertEqual("fact-lifecycle", fact.EvidenceType);
    }

    foreach (var status in TiroStore.FactStatus.AllStatuses)
    {
        var listed = store.ListFacts(status, 10);
        AssertEqual(true, listed.Any(fact => fact.Status == status && fact.Text.Contains(status, StringComparison.Ordinal)));
    }
    return Task.CompletedTask;
});

Run("fact status update is stored and retrievable", () =>
{
    using var store = BuildFixtureStore("fact_update");
    var fact = store.AddFact("Lifecycle update target", "source:fact-update", "test:lifecycle", TiroStore.FactStatus.Active);
    AssertEqual(true, store.UpdateFactStatus(fact.FactId, TiroStore.FactStatus.Stale));
    var stored = store.ListFacts(TiroStore.FactStatus.Stale, 10).Single(item => item.FactId == fact.FactId);
    AssertEqual(TiroStore.FactStatus.Stale, stored.Status);
    return Task.CompletedTask;
});

Run("fact supersession stores explicit relationship", () =>
{
    using var store = BuildFixtureStore("fact_supersession");
    var newer = store.AddFact("Project launch date is May 12", "source:fact-supersession", "test:lifecycle", TiroStore.FactStatus.Active);
    var older = store.AddFact("Project launch date is May 10", "source:fact-supersession", "test:lifecycle", TiroStore.FactStatus.Active);

    AssertEqual(true, store.SupersedeFact(newer.FactId, older.FactId));

    var facts = store.ListFacts(null, 10);
    var storedNewer = facts.Single(fact => fact.FactId == newer.FactId);
    var storedOlder = facts.Single(fact => fact.FactId == older.FactId);
    AssertEqual(older.FactId, storedNewer.SupersedesFactId);
    AssertEqual(newer.FactId, storedOlder.SupersededByFactId);
    AssertEqual(TiroStore.FactStatus.Superseded, storedOlder.Status);
    return Task.CompletedTask;
});

Run("fact conflict is retrieved and reflected in lifecycle query", () =>
{
    using var store = BuildFixtureStore("fact_conflict");
    var first = store.AddFact("Release channel is alpha", "source:fact-conflict", "test:lifecycle", TiroStore.FactStatus.Active);
    var second = store.AddFact("Release channel is beta", "source:fact-conflict", "test:lifecycle", TiroStore.FactStatus.Active);

    store.AddFactConflict(first.FactId, second.FactId);

    var conflicts = store.ListFactConflicts(first.FactId);
    AssertEqual(1, conflicts.Count);
    AssertEqual(Math.Min(first.FactId, second.FactId), conflicts[0].FactId1);
    AssertEqual(Math.Max(first.FactId, second.FactId), conflicts[0].FactId2);

    var queried = store.QueryLifecycleFacts("release channel", 10);
    AssertEqual(true, queried.Any(fact => fact.FactId == first.FactId && fact.Status == TiroStore.FactStatus.Conflicting));
    AssertEqual(true, queried.Any(fact => fact.FactId == second.FactId && fact.Status == TiroStore.FactStatus.Conflicting));
    return Task.CompletedTask;
});

Run("packet includes lifecycle facts and warns when only non-active lifecycle facts match", () =>
{
    using var store = BuildFixtureStore("lifecycle_packet");
    var stale = store.AddFact("Lifecycle-only release window is obsolete", "source:fact-packet", "test:lifecycle", TiroStore.FactStatus.Stale);
    var superseded = store.AddFact("Lifecycle-only release owner is old", "source:fact-packet", "test:lifecycle", TiroStore.FactStatus.Superseded);
    var planner = PlannerRunResult.Disabled(Config(PlannerMode.Off, null, false), "lifecycle-only release");

    var packet = new ContextPacketBuilder(store).Build(
        "lifecycle-only release",
        5,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, null),
        0);

    AssertEqual(true, packet.Facts.Any(fact => fact.FactId == stale.FactId && fact.Status == TiroStore.FactStatus.Stale && fact.EvidenceType == "fact-lifecycle"));
    AssertEqual(true, packet.Facts.Any(fact => fact.FactId == superseded.FactId && fact.Status == TiroStore.FactStatus.Superseded));
    AssertContains(packet.Warnings.Select(warning => warning.Text), "only stale/superseded/conflicting");
    return Task.CompletedTask;
});

Run("current-state policy prefers recent session state over archive memory", () =>
{
    using var store = BuildPolicyStore("policy_current");
    var planner = PlannerRunResult.Disabled(Config(PlannerMode.Off, null, false), "current deployment token owner");
    var packet = new ContextPacketBuilder(store).Build(
        "current deployment token owner",
        5,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, null),
        0,
        "session-alpha");

    AssertEqual("current-state", packet.RetrievalPolicy.QueryMode);
    AssertEqual("session", packet.Facts[0].EvidenceType);
    AssertContains(packet.Warnings.Select(warning => warning.Text), "Retrieval policy mode=current-state");
    var top = packet.RetrievalPolicy.Signals[0];
    AssertEqual("session", top.EvidenceType);
    AssertEqual(true, top.RecencyScore > 0);
    AssertEqual(true, top.RelevanceScore > 0);
    AssertEqual(true, top.Explanation.Contains("relevance", StringComparison.Ordinal));
    AssertEqual(true, top.Explanation.Contains("recency", StringComparison.Ordinal));
    AssertEqual(true, top.Explanation.Contains("importance", StringComparison.Ordinal));
    AssertEqual(true, top.Explanation.Contains("lifecycle", StringComparison.Ordinal));
    return Task.CompletedTask;
});

Run("decision policy prefers operational decision memory", () =>
{
    using var store = BuildPolicyStore("policy_decision");
    var planner = PlannerRunResult.Disabled(Config(PlannerMode.Off, null, false), "deployment token decision");
    var packet = new ContextPacketBuilder(store).Build(
        "deployment token decision",
        5,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, null),
        0,
        "session-alpha");

    AssertEqual("decision", packet.RetrievalPolicy.QueryMode);
    AssertEqual("operational", packet.RetrievalPolicy.Signals[0].EvidenceType);
    AssertEqual("operational", packet.Facts[0].EvidenceType);
    AssertEqual(true, packet.RetrievalPolicy.Signals[0].ImportanceScore >= 40);
    return Task.CompletedTask;
});

Run("archive policy prefers corpus evidence for reference lookup", () =>
{
    using var store = BuildPolicyStore("policy_archive");
    var planner = PlannerRunResult.Disabled(Config(PlannerMode.Off, null, false), "archive deployment token owner");
    var packet = new ContextPacketBuilder(store).Build(
        "archive deployment token owner",
        5,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, null),
        0,
        "session-alpha");

    AssertEqual("archive", packet.RetrievalPolicy.QueryMode);
    AssertEqual("corpus", packet.RetrievalPolicy.Signals[0].EvidenceType);
    AssertEqual("corpus", packet.Facts[0].EvidenceType);
    return Task.CompletedTask;
});

Run("lifecycle policy demotes superseded facts and warns on stale best evidence", () =>
{
    using var store = BuildPolicyStore("policy_lifecycle");
    var stale = store.AddFact(
        "Conflict lifecycle launch window is obsolete",
        "source:policy-fact",
        "test:policy",
        TiroStore.FactStatus.Stale,
        createdUtc: DateTimeOffset.Parse("2026-05-10T00:05:00Z"));
    var superseded = store.AddFact(
        "Conflict lifecycle launch window is replaced",
        "source:policy-fact",
        "test:policy",
        TiroStore.FactStatus.Superseded,
        createdUtc: DateTimeOffset.Parse("2026-05-10T00:06:00Z"));
    var planner = PlannerRunResult.Disabled(Config(PlannerMode.Off, null, false), "conflict lifecycle launch window");

    var packet = new ContextPacketBuilder(store).Build(
        "conflict lifecycle launch window",
        5,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, null),
        0);

    AssertEqual("fact-lifecycle", packet.RetrievalPolicy.QueryMode);
    AssertEqual(stale.FactId, packet.Facts[0].FactId);
    AssertEqual(true, packet.Facts.Any(fact => fact.FactId == superseded.FactId));
    AssertContains(packet.Warnings.Select(warning => warning.Text), "Best weighted evidence has lifecycle limitation");
    return Task.CompletedTask;
});

Run("service query with planner off returns context packet", async () =>
{
    using var store = BuildFixtureStore("service_basic");
    var response = await QueryService(store.DatabasePath, "provenance retrieval", limit: 3);
    AssertEqual(store.DatabasePath, response.DatabasePath);
    AssertEqual("provenance retrieval", response.Query);
    AssertEqual(PlannerMode.Off, response.PlannerMode);
    AssertEqual(true, response.Packet.PrimaryEvidence.Count > 0);
});

Run("service preserves source filter", async () =>
{
    using var store = BuildFixtureStore("service_source_filter");
    var sourceId = "source:m003-quality-notes-txt-fixture-m003-quality-notes-txt";
    var response = await QueryService(store.DatabasePath, "provenance retrieval", sourceId: sourceId);
    AssertEqual(true, response.Packet.PrimaryEvidence.Count > 0);
    AssertEqual(true, response.Packet.PrimaryEvidence.All(result => result.SourceId == sourceId));
    AssertContains(response.Packet.Warnings.Select(warning => warning.Text), "Explicit retrieval filters applied");
});

Run("service preserves document filter", async () =>
{
    using var store = BuildFixtureStore("service_document_filter");
    var response = await QueryService(store.DatabasePath, "packets confidence", documentId: "m003_quality");
    AssertEqual(true, response.Packet.PrimaryEvidence.Count > 0);
    AssertEqual(true, response.Packet.PrimaryEvidence.All(result => result.DocumentId == "m003_quality"));
});

Run("service filter miss is not broadened", async () =>
{
    using var store = BuildFixtureStore("service_filter_miss");
    var response = await QueryService(store.DatabasePath, "provenance retrieval", sourceId: "source:not-present", contextWindow: 1);
    AssertEqual(0, response.Packet.PrimaryEvidence.Count);
    AssertEqual(0, response.Packet.SupportingContext.Count);
    AssertContains(response.Packet.Unknowns.Select(unknown => unknown.Text), "No stored chunks matched the requested source/document filters.");
    AssertContains(response.Packet.Warnings.Select(warning => warning.Text), "not silently broadened");
});

Run("service preserves context window", async () =>
{
    using var store = BuildFixtureStore("service_context_window");
    var response = await QueryService(store.DatabasePath, "packets confidence warnings sources", limit: 1, documentId: "m003_quality", contextWindow: 1);
    AssertEqual(1, response.Packet.PrimaryEvidence.Count);
    AssertEqual(true, response.Packet.SupportingContext.Count > 0);
    AssertEqual(response.Packet.PrimaryEvidence[0].ChunkId, response.Packet.SupportingContext[0].PrimaryChunkId);
    AssertEqual(true, response.Packet.SupportingContext.All(context => context.Distance <= 1));
});

Run("service preserves session lane", async () =>
{
    using var store = BuildSessionStore("service_session");
    var response = await QueryService(store.DatabasePath, "deployment token", sessionId: "session-alpha");
    AssertEqual("session-alpha", response.Packet.SessionId);
    AssertEqual(true, response.Packet.SessionEvidence.Count > 0);
    AssertEqual(true, response.Packet.Facts.Any(fact => fact.EvidenceType == "session"));
});

Run("service preserves operational memory lane", async () =>
{
    using var store = BuildOperationalStore("service_operational");
    var response = await QueryService(store.DatabasePath, "deployment token owner", sessionId: "session-alpha");
    AssertEqual(true, response.Packet.OperationalMemory.Count > 0);
    AssertEqual(true, response.Packet.Facts.Any(fact => fact.EvidenceType == "operational"));
});

Run("service preserves lifecycle facts", async () =>
{
    using var store = BuildFixtureStore("service_lifecycle");
    var stale = store.AddFact("Service lifecycle owner is obsolete", "source:service-lifecycle", "test:service", TiroStore.FactStatus.Stale);
    var conflicting = store.AddFact("Service lifecycle owner is disputed", "source:service-lifecycle", "test:service", TiroStore.FactStatus.Conflicting);
    var response = await QueryService(store.DatabasePath, "service lifecycle owner");
    AssertEqual(true, response.Packet.Facts.Any(fact => fact.FactId == stale.FactId && fact.Status == TiroStore.FactStatus.Stale));
    AssertEqual(true, response.Packet.Facts.Any(fact => fact.FactId == conflicting.FactId && fact.Status == TiroStore.FactStatus.Conflicting));
    AssertContains(response.Packet.Warnings.Select(warning => warning.Text), "only stale/superseded/conflicting");
});

Run("service preserves retrieval policy", async () =>
{
    using var store = BuildPolicyStore("service_policy");
    var response = await QueryService(store.DatabasePath, "current deployment token owner", sessionId: "session-alpha");
    AssertEqual("current-state", response.Packet.RetrievalPolicy.QueryMode);
    AssertEqual(true, response.Packet.RetrievalPolicy.Signals.Count > 0);
    var explanation = response.Packet.RetrievalPolicy.Signals[0].Explanation;
    AssertEqual(true, explanation.Contains("relevance", StringComparison.Ordinal));
    AssertEqual(true, explanation.Contains("recency", StringComparison.Ordinal));
    AssertEqual(true, explanation.Contains("importance", StringComparison.Ordinal));
    AssertEqual(true, explanation.Contains("lifecycle", StringComparison.Ordinal));
});

Run("service rejects empty query", async () =>
{
    await AssertThrowsAsync(() => QueryService(Path.Combine("/tmp", $"tiro_empty_query_{Guid.NewGuid():N}.sqlite3"), " "));
});

Run("service rejects invalid context window", async () =>
{
    using var store = BuildFixtureStore("service_invalid_context");
    await AssertThrowsAsync(() => QueryService(store.DatabasePath, "provenance retrieval", contextWindow: -1));
    await AssertThrowsAsync(() => QueryService(store.DatabasePath, "provenance retrieval", contextWindow: 3));
});

Run("service rejects invalid limit", async () =>
{
    using var store = BuildFixtureStore("service_invalid_limit");
    await AssertThrowsAsync(() => QueryService(store.DatabasePath, "provenance retrieval", limit: 0));
    await AssertThrowsAsync(() => QueryService(store.DatabasePath, "provenance retrieval", limit: -1));
});

Run("service does not mutate memory during query", async () =>
{
    using var store = BuildOperationalStore("service_no_mutation");
    store.AddFact("No mutation lifecycle fact", "source:no-mutation", "test:service", TiroStore.FactStatus.Active);
    var beforeStats = store.GetStats();
    var beforeFacts = store.ListFacts(null, 100).Count;

    _ = await QueryService(store.DatabasePath, "deployment token owner", sessionId: "session-alpha");

    var afterStats = store.GetStats();
    var afterFacts = store.ListFacts(null, 100).Count;
    AssertEqual(beforeStats.SessionCount, afterStats.SessionCount);
    AssertEqual(beforeStats.MessageCount, afterStats.MessageCount);
    AssertEqual(beforeStats.OperationalRecordCount, afterStats.OperationalRecordCount);
    AssertEqual(beforeFacts, afterFacts);
});

Run("ingest session note creates session message", async () =>
{
    using var store = EmptyStore("m012_session_note");
    var beforeStats = store.GetStats();
    var beforeFacts = store.ListFacts(null, 100).Count;
    var response = new TiroIngestStateService().IngestSessionNote(new TiroSessionNoteIngestRequest(
        store.DatabasePath,
        "m012-session",
        "M012 session note records controlled state ingestion.",
        "operator:fixture",
        "operator",
        DateTimeOffset.Parse("2026-05-10T01:00:00Z")));

    AssertEqual("ok", response.Status);
    AssertEqual("session_note", response.Mode);
    AssertEqual(1, response.MessagesWritten);
    AssertEqual(0, response.OperationalRecordsWritten);
    AssertEqual("operator:fixture", response.SourceIdentity);
    AssertEqual(0, store.ListOperationalRecords("decision", "m012-session", 10).Count);
    AssertEqual(beforeFacts, store.ListFacts(null, 100).Count);
    AssertEqual(beforeStats.ChunkCount, store.GetStats().ChunkCount);

    var query = await QueryService(store.DatabasePath, "controlled state ingestion", sessionId: "m012-session");
    AssertEqual(true, query.Packet.SessionEvidence.Count > 0);
    AssertEqual(true, query.Packet.SessionEvidence.Any(item => item.SourceIdentity == "operator:fixture"));
});

Run("ingest operational decision creates operational record", async () =>
{
    using var store = EmptyStore("m012_operational_decision");
    var beforeStats = store.GetStats();
    var beforeFacts = store.ListFacts(null, 100).Count;
    var response = new TiroIngestStateService().IngestOperationalRecord(new TiroOperationalRecordIngestRequest(
        store.DatabasePath,
        "decision",
        "Decision: M012 keeps write-side ingestion explicit.",
        "operator:fixture",
        "m012-session",
        DateTimeOffset.Parse("2026-05-10T01:01:00Z")));

    AssertEqual("ok", response.Status);
    AssertEqual("operational_record", response.Mode);
    AssertEqual(1, response.OperationalRecordsWritten);
    AssertEqual(0, response.MessagesWritten);
    AssertEqual("decision", response.WrittenItems[0].RecordType);
    AssertEqual("operator:fixture", response.WrittenItems[0].SourceIdentity);
    AssertEqual("m012-session", response.SessionId);
    AssertEqual(beforeFacts, store.ListFacts(null, 100).Count);
    AssertEqual(beforeStats.ChunkCount, store.GetStats().ChunkCount);

    var query = await QueryService(store.DatabasePath, "write-side ingestion explicit", sessionId: "m012-session");
    AssertEqual(true, query.Packet.OperationalMemory.Any(item => item.RecordType == "decision"));
});

Run("ingest operational todo warning unknown", async () =>
{
    using var store = EmptyStore("m012_operational_types");
    foreach (var recordType in new[] { "todo", "warning", "unknown" })
    {
        var response = new TiroIngestStateService().IngestOperationalRecord(new TiroOperationalRecordIngestRequest(
            store.DatabasePath,
            recordType,
            $"{recordType}: M012 operational record retrieval marker.",
            "operator:fixture",
            "m012-session",
            DateTimeOffset.Parse("2026-05-10T01:02:00Z")));
        AssertEqual("ok", response.Status);
        AssertEqual(recordType, response.WrittenItems[0].RecordType);
    }

    var query = await QueryService(store.DatabasePath, "M012 operational record retrieval marker", sessionId: "m012-session");
    AssertEqual(true, query.Packet.OperationalMemory.Any(item => item.RecordType == "todo"));
    AssertEqual(true, query.Packet.OperationalMemory.Any(item => item.RecordType == "warning"));
    AssertEqual(true, query.Packet.OperationalMemory.Any(item => item.RecordType == "unknown"));
});

Run("ingest rejects invalid record type", () =>
{
    using var store = EmptyStore("m012_invalid_record_type");
    AssertThrows(() => new TiroIngestStateService().IngestOperationalRecord(new TiroOperationalRecordIngestRequest(
        store.DatabasePath,
        "fact",
        "Invalid fact write attempt.",
        "operator:fixture")));
    AssertThrows(() => new TiroIngestStateService().IngestOperationalRecord(new TiroOperationalRecordIngestRequest(
        store.DatabasePath,
        "random",
        "Invalid random write attempt.",
        "operator:fixture")));
    return Task.CompletedTask;
});

Run("ingest rejects missing session note text", () =>
{
    using var store = EmptyStore("m012_missing_note_text");
    AssertThrows(() => new TiroIngestStateService().IngestSessionNote(new TiroSessionNoteIngestRequest(
        store.DatabasePath,
        "m012-session",
        " ",
        "operator:fixture")));
    return Task.CompletedTask;
});

Run("ingest rejects missing saved session file", () =>
{
    using var store = EmptyStore("m012_missing_session_file");
    AssertThrows(() => new TiroIngestStateService().IngestAichatSession(new TiroAichatSessionIngestRequest(
        store.DatabasePath,
        "m012-session",
        Path.Combine("/tmp", $"missing_session_{Guid.NewGuid():N}.yaml"),
        "aichat:fixture_runtime")));
    return Task.CompletedTask;
});

Run("saved AIChat session imports as session evidence", async () =>
{
    using var store = EmptyStore("m012_aichat_session");
    var beforeStats = store.GetStats();
    var beforeFacts = store.ListFacts(null, 100).Count;
    var fixture = Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures", "session_test.yaml");
    var response = new TiroIngestStateService().IngestAichatSession(new TiroAichatSessionIngestRequest(
        store.DatabasePath,
        "m012-session-test",
        fixture,
        "aichat:fixture_runtime",
        DateTimeOffset.Parse("2026-05-10T01:03:00Z")));

    AssertEqual("ok", response.Status);
    AssertEqual("aichat_saved_session", response.Mode);
    AssertEqual(true, response.MessagesWritten > 0);
    AssertEqual(0, response.OperationalRecordsWritten);
    AssertEqual(0, response.FactsWritten);
    AssertEqual(0, response.ChunksWritten);
    AssertEqual(Path.GetFullPath(fixture), response.SelectedFile);
    AssertEqual("m012-session-test", response.SessionId);
    AssertEqual("aichat:fixture_runtime", response.SourceIdentity);
    AssertEqual(beforeFacts, store.ListFacts(null, 100).Count);
    var afterStats = store.GetStats();
    AssertEqual(beforeStats.ChunkCount, afterStats.ChunkCount);
    AssertEqual(beforeStats.DocumentCount, afterStats.DocumentCount);
    AssertEqual(beforeStats.SourceCount, afterStats.SourceCount);

    var query = await QueryService(store.DatabasePath, "session_test", sessionId: "m012-session-test");
    AssertEqual(true, query.Packet.SessionEvidence.Count > 0);
    AssertEqual(true, query.Packet.SessionEvidence.Any(item => item.SourceIdentity == "aichat:fixture_runtime"));
    AssertEqual(0, query.Packet.PrimaryEvidence.Count);
});

Run("cli ingest commands emit structured json", () =>
{
    using var store = EmptyStore("m012_cli_ingest");
    var note = RunCli(
        "--db",
        store.DatabasePath,
        "--session-id",
        "m012-cli",
        "ingest-session-note",
        "--source-identity",
        "operator:fixture",
        "--text",
        "CLI session note marker for M012.");
    AssertEqual(0, note.ExitCode);
    var noteResponse = JsonSerializer.Deserialize<TiroIngestStateResponse>(note.Stdout, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("CLI note output did not deserialize.");
    AssertEqual("ok", noteResponse.Status);
    AssertEqual(1, noteResponse.MessagesWritten);

    var operational = RunCli(
        "--db",
        store.DatabasePath,
        "--session-id",
        "m012-cli",
        "ingest-operational-record",
        "--record-type",
        "warning",
        "--source-identity",
        "operator:fixture",
        "--text",
        "CLI warning marker for M012.");
    AssertEqual(0, operational.ExitCode);
    var operationalResponse = JsonSerializer.Deserialize<TiroIngestStateResponse>(operational.Stdout, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("CLI operational output did not deserialize.");
    AssertEqual("ok", operationalResponse.Status);
    AssertEqual(1, operationalResponse.OperationalRecordsWritten);
    return Task.CompletedTask;
});

Run("cli query still works after service refactor", () =>
{
    using var store = BuildFixtureStore("service_cli");
    var result = RunCli(
        "--db",
        store.DatabasePath,
        "--planner",
        "off",
        "--limit",
        "1",
        "query",
        "provenance",
        "retrieval");
    AssertEqual(0, result.ExitCode);
    var packet = JsonSerializer.Deserialize<ContextPacket>(result.Stdout, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("CLI output did not deserialize to ContextPacket.");
    AssertEqual(true, packet.PrimaryEvidence.Count > 0);
    AssertEqual(true, result.Stderr.Contains("Planner key", StringComparison.Ordinal));
    return Task.CompletedTask;
});

Run("cli stats includes memory lane counts", () =>
{
    using var store = EmptyStore("m013_cli_stats");
    store.AddOperationalRecord("decision", "M013 stats sanity marker.", "test:m013");
    var result = RunCli(
        "--db",
        store.DatabasePath,
        "stats");
    AssertEqual(0, result.ExitCode);
    var stats = JsonSerializer.Deserialize<StoreStats>(result.Stdout, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("CLI stats output did not deserialize.");
    AssertEqual(1, stats.OperationalRecordCount);
    AssertEqual(0, stats.FactCount);
    AssertEqual(0, stats.FactConflictCount);
    return Task.CompletedTask;
});

Run("inspect sessions inventories stored sessions newest first", () =>
{
    using var store = EmptyStore("m020_inspect_sessions");
    store.CreateSession("older-session", DateTimeOffset.Parse("2026-05-13T00:00:00Z"));
    store.IngestMessage("older-session", "user", "test:m020", "older session marker", DateTimeOffset.Parse("2026-05-13T00:01:00Z"));
    store.CreateSession("newer-session", DateTimeOffset.Parse("2026-05-13T00:02:00Z"));
    store.IngestMessage("newer-session", "assistant", "test:m020", "newer session marker", DateTimeOffset.Parse("2026-05-13T00:03:00Z"));

    var result = RunCli("--db", store.DatabasePath, "inspect", "sessions", "--limit", "2");
    AssertEqual(0, result.ExitCode);
    using var doc = JsonDocument.Parse(result.Stdout);
    var root = doc.RootElement;
    AssertEqual("ok", root.GetProperty("status").GetString());
    AssertEqual(2, root.GetProperty("session_count").GetInt32());
    AssertEqual("newer-session", root.GetProperty("sessions")[0].GetProperty("session_id").GetString());
    AssertEqual(1, root.GetProperty("sessions")[0].GetProperty("message_count").GetInt32());
    return Task.CompletedTask;
});

Run("inspect operational filters record type and session", () =>
{
    using var store = BuildOperationalStore("m020_inspect_operational");
    var result = RunCli(
        "--db",
        store.DatabasePath,
        "inspect",
        "operational",
        "--record-type",
        "warning",
        "--session-id",
        "session-alpha",
        "--limit",
        "5");
    AssertEqual(0, result.ExitCode);
    using var doc = JsonDocument.Parse(result.Stdout);
    var root = doc.RootElement;
    AssertEqual("ok", root.GetProperty("status").GetString());
    AssertEqual("warning", root.GetProperty("filters").GetProperty("record_type").GetString());
    AssertEqual(true, root.GetProperty("record_count").GetInt32() >= 1);
    AssertEqual(true, root.GetProperty("records").EnumerateArray().All(item => item.GetProperty("record_type").GetString() == "warning"));
    AssertEqual(true, root.GetProperty("records").EnumerateArray().All(item => item.GetProperty("session_id").GetString() == "session-alpha"));
    return Task.CompletedTask;
});

Run("session-summary returns chronological messages and missing session is structured", () =>
{
    using var store = EmptyStore("m020_session_summary");
    store.CreateSession("summary-session", DateTimeOffset.Parse("2026-05-13T00:00:00Z"));
    store.IngestMessage("summary-session", "user", "test:m020", "first chronological marker", DateTimeOffset.Parse("2026-05-13T00:01:00Z"));
    store.IngestMessage("summary-session", "assistant", "test:m020", "second chronological marker", DateTimeOffset.Parse("2026-05-13T00:02:00Z"));

    var result = RunCli("--db", store.DatabasePath, "--limit", "1", "session-summary", "summary-session");
    AssertEqual(0, result.ExitCode);
    using var doc = JsonDocument.Parse(result.Stdout);
    var root = doc.RootElement;
    AssertEqual("ok", root.GetProperty("status").GetString());
    AssertEqual(2, root.GetProperty("message_count_total").GetInt32());
    AssertEqual(1, root.GetProperty("message_count_returned").GetInt32());
    AssertEqual("first chronological marker", root.GetProperty("messages")[0].GetProperty("text").GetString());
    AssertEqual(true, root.GetProperty("warnings").EnumerateArray().Any());

    var missing = RunCli("--db", store.DatabasePath, "session-summary", "missing-session");
    AssertEqual(0, missing.ExitCode);
    using var missingDoc = JsonDocument.Parse(missing.Stdout);
    AssertEqual("not_found", missingDoc.RootElement.GetProperty("status").GetString());
    return Task.CompletedTask;
});

Run("phrase-search finds exact phrase by lane and honors session filter", () =>
{
    using var store = EmptyStore("m020_phrase_search");
    store.CreateSession("phrase-session", DateTimeOffset.Parse("2026-05-13T00:00:00Z"));
    store.IngestMessage("phrase-session", "user", "test:m020", "the filing cabinet remembers exact phrase alpha", DateTimeOffset.Parse("2026-05-13T00:01:00Z"));
    store.AddOperationalRecord("warning", "Warning: filing cabinet exact phrase beta", "test:m020", "phrase-session", createdUtc: DateTimeOffset.Parse("2026-05-13T00:02:00Z"));

    var sessionResult = RunCli("--db", store.DatabasePath, "--session-id", "phrase-session", "phrase-search", "filing cabinet remembers", "--lane", "session");
    AssertEqual(0, sessionResult.ExitCode);
    using var sessionDoc = JsonDocument.Parse(sessionResult.Stdout);
    AssertEqual(1, sessionDoc.RootElement.GetProperty("result_count").GetInt32());
    AssertEqual("session", sessionDoc.RootElement.GetProperty("results")[0].GetProperty("lane").GetString());

    var wrongSession = RunCli("--db", store.DatabasePath, "--session-id", "wrong-session", "phrase-search", "filing cabinet remembers", "--lane", "session");
    AssertEqual(0, wrongSession.ExitCode);
    using var wrongDoc = JsonDocument.Parse(wrongSession.Stdout);
    AssertEqual(0, wrongDoc.RootElement.GetProperty("result_count").GetInt32());

    var operational = RunCli("--db", store.DatabasePath, "phrase-search", "filing cabinet exact phrase beta", "--lane", "operational");
    AssertEqual(0, operational.ExitCode);
    using var operationalDoc = JsonDocument.Parse(operational.Stdout);
    AssertEqual(1, operationalDoc.RootElement.GetProperty("result_count").GetInt32());
    AssertEqual("operational", operationalDoc.RootElement.GetProperty("results")[0].GetProperty("lane").GetString());
    return Task.CompletedTask;
});

Run("proxy schema initializes on build and inspect filters work", () =>
{
    using var store = BuildAlephStore("m022_proxy_schema");
    var service = new TiroProxyService();
    var build = service.BuildCorpusProxies(new TiroProxyBuildRequest(
        store.DatabasePath,
        "corpus",
        "phrack49-file14-smashing-the-stack-for-fun-and-profit",
        null,
        Rebuild: false));
    AssertEqual("ok", build.Status);
    AssertEqual(true, build.ProxiesCreated >= 2);
    AssertEqual(true, build.PointersCreated >= 2);

    var inspect = service.Inspect(store.DatabasePath, "corpus", "phrack49-file14-smashing-the-stack-for-fun-and-profit", null, "active", 50);
    AssertEqual("ok", inspect.Status);
    AssertEqual(true, inspect.ProxyCount >= 2);
    AssertEqual(true, inspect.Proxies.All(proxy => proxy.Lane == "corpus"));
    AssertEqual(true, inspect.Proxies.All(proxy => proxy.DocumentId == "phrack49-file14-smashing-the-stack-for-fun-and-profit"));
    AssertEqual(true, inspect.Proxies.All(proxy => proxy.Status == "active"));
    return Task.CompletedTask;
});

Run("proxy build does not duplicate without rebuild and rebuild supersedes prior active proxies", () =>
{
    using var store = BuildAlephStore("m022_proxy_rebuild");
    var service = new TiroProxyService();
    var first = service.BuildCorpusProxies(new TiroProxyBuildRequest(
        store.DatabasePath,
        "corpus",
        "phrack49-file14-smashing-the-stack-for-fun-and-profit",
        null,
        Rebuild: false));
    AssertEqual(true, first.ProxiesCreated > 0);

    var second = service.BuildCorpusProxies(new TiroProxyBuildRequest(
        store.DatabasePath,
        "corpus",
        "phrack49-file14-smashing-the-stack-for-fun-and-profit",
        null,
        Rebuild: false));
    AssertEqual(0, second.ProxiesCreated);
    AssertContains(second.Warnings, "Active proxies already exist");

    var rebuilt = service.BuildCorpusProxies(new TiroProxyBuildRequest(
        store.DatabasePath,
        "corpus",
        "phrack49-file14-smashing-the-stack-for-fun-and-profit",
        null,
        Rebuild: true));
    AssertEqual(true, rebuilt.ProxiesSuperseded > 0);

    var superseded = service.Inspect(store.DatabasePath, "corpus", "phrack49-file14-smashing-the-stack-for-fun-and-profit", null, "superseded", 100);
    AssertEqual(true, superseded.ProxyCount > 0);
    return Task.CompletedTask;
});

Run("proxy hydrate returns original chunk text and invalid pointer is structured", () =>
{
    using var store = BuildAlephProxyStore("m022_proxy_hydrate");
    var inspect = new TiroProxyService().Inspect(store.DatabasePath, "corpus", "phrack49-file14-smashing-the-stack-for-fun-and-profit", null, "active", 100);
    var chunkProxy = inspect.Proxies.First(proxy => proxy.ProxyType == "corpus_chunk_group");

    var hydrated = JsonSerializer.SerializeToDocument(new TiroProxyService().Hydrate(store.DatabasePath, chunkProxy.PointerId));
    AssertEqual("ok", hydrated.RootElement.GetProperty("status").GetString());
    AssertEqual(chunkProxy.PointerId, hydrated.RootElement.GetProperty("evidence").GetProperty("pointer_id").GetString());
    AssertEqual(true, hydrated.RootElement.GetProperty("evidence").GetProperty("text").GetString()!.Length > 50);

    var missing = JsonSerializer.SerializeToDocument(new TiroProxyService().Hydrate(store.DatabasePath, "pointer:not-real"));
    AssertEqual("not_found", missing.RootElement.GetProperty("status").GetString());
    return Task.CompletedTask;
});

Run("proxy recall finds Aleph One document and NOP sled topic and preserves negative control", () =>
{
    using var store = BuildAlephProxyStore("m022_proxy_recall");
    var service = new TiroProxyService();

    var documentRecall = service.Recall(new TiroProxyRecallRequest(store.DatabasePath, "stack smashing article", 5));
    AssertEqual("ok", documentRecall.Status);
    AssertEqual(true, documentRecall.Proxies.Any(proxy => proxy.DocumentId == "phrack49-file14-smashing-the-stack-for-fun-and-profit"));
    AssertEqual(true, documentRecall.HydratedEvidence.Any(item => item.DocumentId == "phrack49-file14-smashing-the-stack-for-fun-and-profit"));

    var topicRecall = service.Recall(new TiroProxyRecallRequest(store.DatabasePath, "NOP sled shellcode", 5));
    AssertEqual(true, topicRecall.HydratedEvidence.Any(item => item.TargetLane == "corpus" && item.Text.Contains("NOP", StringComparison.OrdinalIgnoreCase)));

    var negative = service.Recall(new TiroProxyRecallRequest(store.DatabasePath, "purple router goblins", 5));
    AssertEqual(true, negative.HydratedEvidenceCount == 0 || negative.Unknowns.Count > 0);
    AssertContains(negative.Unknowns, "No proxy-backed authoritative evidence matched the query");
    return Task.CompletedTask;
});

Run("recall integrates proxy evidence and falls back cleanly when proxies are absent", () =>
{
    using var proxyStore = BuildAlephProxyStore("m022_recall_integration");
    var withProxies = new TiroRecallService().Recall(new TiroRecallRequest(proxyStore.DatabasePath, "stack smashing article", 5, PlannerMode.Off));
    AssertEqual(true, withProxies.ProxyCandidates.Count > 0);
    AssertEqual(true, withProxies.ProxyHydratedEvidence.Count > 0);
    AssertEqual(true, withProxies.Evidence.Any(item => item.ProxyId is not null));

    using var noProxyStore = BuildAlephStore("m022_recall_fallback");
    var withoutProxies = new TiroRecallService().Recall(new TiroRecallRequest(noProxyStore.DatabasePath, "stack smashing article", 5, PlannerMode.Off));
    AssertContains(withoutProxies.Warnings, "No recall proxies available; fallback lexical recall used.");
    return Task.CompletedTask;
});

Run("tiro_query warns on inventory and whole-session requests", async () =>
{
    using var store = EmptyStore("m020_query_warnings");
    store.CreateSession("warning-session", DateTimeOffset.Parse("2026-05-13T00:00:00Z"));
    store.IngestMessage("warning-session", "user", "test:m020", "whole session warning marker", DateTimeOffset.Parse("2026-05-13T00:01:00Z"));

    var inventory = await QueryService(store.DatabasePath, "list sessions", plannerMode: PlannerMode.Off);
    AssertContains(inventory.Packet.Warnings.Select(warning => warning.Text), "Use tiro_inspect sessions");

    var summary = await QueryService(store.DatabasePath, "summarize session", sessionId: "warning-session", plannerMode: PlannerMode.Off);
    AssertContains(summary.Packet.Warnings.Select(warning => warning.Text), "Use tiro_session_summary");
});

Run("search-debug returns lane diagnostics", () =>
{
    using var store = BuildOperationalStore("m013_search_debug");
    var result = RunCli(
        "--db",
        store.DatabasePath,
        "--planner",
        "off",
        "--session-id",
        "session-alpha",
        "search-debug",
        "deployment",
        "token",
        "owner");
    AssertEqual(0, result.ExitCode);
    var diagnostics = JsonSerializer.Deserialize<TiroSearchDiagnostics>(result.Stdout, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("search-debug output did not deserialize.");
    AssertEqual("deployment token owner", diagnostics.OriginalQuery);
    AssertEqual(true, diagnostics.NormalizedQueryTerms.Contains("deployment"));
    AssertEqual(true, diagnostics.LanesSearched.Contains("session"));
    AssertEqual(true, diagnostics.CandidateCounts.Session > 0);
    AssertEqual(true, diagnostics.CandidateCounts.Operational > 0);
    return Task.CompletedTask;
});

Run("proof-carry-forward command proves durable operational memory", () =>
{
    using var store = EmptyStore("m013_proof_cli");
    var token = $"m013proofclitest{Guid.NewGuid():N}";
    var phrase = $"proof carry forward alpha {token}";
    var result = RunCli(
        "--db",
        store.DatabasePath,
        "proof-carry-forward",
        "--phrase",
        phrase,
        "--source-identity",
        "test:m013");
    AssertEqual(0, result.ExitCode);
    var proof = JsonSerializer.Deserialize<TiroProofCarryForwardResult>(result.Stdout, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("proof-carry-forward output did not deserialize.");
    AssertEqual("PASS", proof.Status);
    AssertEqual(0, proof.BeforeCount);
    AssertEqual(true, proof.AfterCount > 0);
    AssertEqual(true, proof.FreshProcessCount > 0);
    return Task.CompletedTask;
});

Run("semantic expansion retrieves session memory natural query", () =>
{
    using var store = BuildSemanticStore("m015_session_memory");
    var planner = SemanticPlanner(
        "how does session memory work",
        "session evidence checkpoint ingestion",
        "session_recall",
        new[] { "session evidence saved session checkpoint", "conversation transcript ingestion", "AIChat saved session memory" },
        new[] { "session", "evidence", "checkpoint", "ingestion", "saved" },
        new[] { "session" });
    var packet = new ContextPacketBuilder(store).Build(
        "how does session memory work",
        5,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, null),
        0,
        "semantic-session");
    AssertEqual(true, packet.SessionEvidence.Count > 0);
    AssertEqual("session_recall", packet.Planner.SemanticIntent);
    AssertEqual(true, packet.Planner.ExpandedQueries.Count > 1);
    return Task.CompletedTask;
});

Run("semantic expansion retrieves operational decision about write tool", () =>
{
    using var store = BuildSemanticStore("m015_write_tool");
    var planner = SemanticPlanner(
        "what did we decide about the write tool",
        "state ingestion write side tooling decision",
        "decision_lookup",
        new[] { "state ingestion write tool decision", "controlled write side tooling", "tiro ingest state decision" },
        new[] { "state", "ingestion", "write", "tool", "decision" },
        new[] { "operational" });
    var packet = new ContextPacketBuilder(store).Build(
        "what did we decide about the write tool",
        5,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, null),
        0,
        null);
    AssertEqual(true, packet.OperationalMemory.Any(item => item.RecordType == "decision"));
    AssertEqual(true, packet.Facts.Any(fact => fact.EvidenceType == "operational"));
    return Task.CompletedTask;
});

Run("semantic expansion retrieves operational warning about broad tools", () =>
{
    using var store = BuildSemanticStore("m015_broad_tools");
    var planner = SemanticPlanner(
        "what warnings were recorded about broad tools",
        "broad tool exposure warning",
        "warning_lookup",
        new[] { "broad tools warning constrained exposure", "tool exposure risk warning" },
        new[] { "broad", "tool", "warning", "exposure" },
        new[] { "operational" });
    var packet = new ContextPacketBuilder(store).Build(
        "what warnings were recorded about broad tools",
        5,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, null),
        0,
        null);
    AssertEqual(true, packet.OperationalMemory.Any(item => item.RecordType == "warning"));
    return Task.CompletedTask;
});

Run("semantic session summary warns when fragmentary", () =>
{
    using var store = BuildSemanticStore("m015_session_summary");
    var planner = SemanticPlanner(
        "summarize the saved session",
        "saved session summary checkpoint evidence",
        "session_summary",
        new[] { "saved session summary", "checkpointed conversation evidence" },
        new[] { "saved", "session", "summary", "checkpoint" },
        new[] { "session" });
    var packet = new ContextPacketBuilder(store).Build(
        "summarize the saved session",
        5,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, null),
        0,
        "semantic-session");
    AssertEqual(true, packet.SessionEvidence.Count > 0);
    AssertContains(packet.Warnings.Select(item => item.Text), "Planner warning");
    return Task.CompletedTask;
});

Run("semantic expansion does not broaden explicit session filter", () =>
{
    using var store = BuildSemanticStore("m015_filter_session");
    var planner = SemanticPlanner(
        "how does session memory work",
        "session evidence checkpoint ingestion",
        "session_recall",
        new[] { "session evidence saved session checkpoint", "conversation transcript ingestion" },
        new[] { "session", "evidence", "checkpoint" },
        new[] { "session" });
    var packet = new ContextPacketBuilder(store).Build(
        "how does session memory work",
        5,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, null),
        0,
        "wrong-session");
    AssertEqual(0, packet.SessionEvidence.Count);
    AssertContains(packet.Unknowns.Select(item => item.Text), "No recent session/state messages are stored for session 'wrong-session'.");
    return Task.CompletedTask;
});

Run("semantic negative control remains negative", () =>
{
    using var store = BuildSemanticStore("m015_negative");
    var planner = SemanticPlanner(
        "purple router goblins ate the firewall",
        "purple router goblins firewall",
        "negative_control",
        new[] { "purple router goblins firewall" },
        new[] { "purple", "router", "goblins", "firewall" },
        new[] { "session", "operational" });
    var packet = new ContextPacketBuilder(store).Build(
        "purple router goblins ate the firewall",
        5,
        planner,
        "GEMINI_API_KEY",
        new RetrievalFilters(null, null),
        0,
        "semantic-session");
    AssertEqual(0, packet.SessionEvidence.Count + packet.OperationalMemory.Count + packet.PrimaryEvidence.Count);
    AssertEqual("none", packet.Confidence);
    return Task.CompletedTask;
});

Run("search-debug shows semantic expansion diagnostics", () =>
{
    using var store = BuildSemanticStore("m015_search_debug");
    var result = RunCli(
        "--db",
        store.DatabasePath,
        "--planner",
        "off",
        "--session-id",
        "semantic-session",
        "search-debug",
        "session",
        "checkpoint");
    AssertEqual(0, result.ExitCode);
    var diagnostics = JsonSerializer.Deserialize<TiroSearchDiagnostics>(result.Stdout, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("search-debug output did not deserialize.");
    AssertEqual(true, diagnostics.ExpandedQueries.Count > 0);
    AssertEqual(true, diagnostics.ExpandedQueryDiagnostics.Count > 0);
    AssertEqual(true, diagnostics.CandidateCounts.Session > 0);
    return Task.CompletedTask;
});

Run("inspect aichat sessions finds newest", () =>
{
    var dir = CreateSessionFixtureDir("m014_inspect");
    var older = WriteSessionFixture(dir, "older_session.yaml", "older phrase", "2026-05-10T00:00:00Z");
    var newer = WriteSessionFixture(dir, "new session.yaml", "newer phrase", "2026-05-10T00:01:00Z");
    File.SetLastWriteTimeUtc(older, DateTime.Parse("2026-05-10T00:00:00Z").ToUniversalTime());
    File.SetLastWriteTimeUtc(newer, DateTime.Parse("2026-05-10T00:01:00Z").ToUniversalTime());

    var result = RunCli("inspect-aichat-sessions", "--sessions-dir", dir);
    AssertEqual(0, result.ExitCode);
    var inspection = JsonSerializer.Deserialize<AichatSessionInspection>(result.Stdout, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("inspect output did not deserialize.");
    AssertEqual(2, inspection.CandidateCount);
    AssertEqual(Path.GetFullPath(newer), inspection.NewestFile);
    AssertEqual("new_session", inspection.Candidates[0].DerivedSessionId);
    return Task.CompletedTask;
});

Run("explicit saved session ingest reports checkpoint metadata", async () =>
{
    using var store = EmptyStore("m014_explicit_ingest");
    var dir = CreateSessionFixtureDir("m014_explicit");
    var file = WriteSessionFixture(dir, "explicit_session.yaml", "m014 explicit distinctive phrase", "2026-05-10T00:00:00Z");
    var result = RunCli(
        "--db",
        store.DatabasePath,
        "--session-id",
        "explicit-session-id",
        "ingest-aichat-session",
        "--source-identity",
        "session_fixture_runtime",
        "--file",
        file);
    AssertEqual(0, result.ExitCode);
    var response = JsonSerializer.Deserialize<TiroIngestStateResponse>(result.Stdout, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("ingest output did not deserialize.");
    AssertEqual("ok", response.Status);
    AssertEqual(Path.GetFullPath(file), response.SelectedFile);
    AssertEqual("explicit-session-id", response.SessionId);
    AssertEqual(true, response.MessagesWritten > 0);
    AssertEqual(0, response.FactsWritten);
    AssertEqual(0, response.ChunksWritten);

    var query = await QueryService(store.DatabasePath, "m014 explicit distinctive", sessionId: "explicit-session-id");
    AssertEqual(true, query.Packet.SessionEvidence.Count > 0);
});

Run("latest saved session ingest derives session id", () =>
{
    using var store = EmptyStore("m014_latest_ingest");
    var dir = CreateSessionFixtureDir("m014_latest");
    var older = WriteSessionFixture(dir, "old_session.yaml", "old phrase", "2026-05-10T00:00:00Z");
    var newer = WriteSessionFixture(dir, "newest session.yaml", "latest distinctive phrase", "2026-05-10T00:01:00Z");
    File.SetLastWriteTimeUtc(older, DateTime.Parse("2026-05-10T00:00:00Z").ToUniversalTime());
    File.SetLastWriteTimeUtc(newer, DateTime.Parse("2026-05-10T00:01:00Z").ToUniversalTime());

    var result = RunCli(
        "--db",
        store.DatabasePath,
        "ingest-aichat-session",
        "--source-identity",
        "session_fixture_runtime",
        "--latest",
        "--sessions-dir",
        dir);
    AssertEqual(0, result.ExitCode);
    var response = JsonSerializer.Deserialize<TiroIngestStateResponse>(result.Stdout, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("latest ingest output did not deserialize.");
    AssertEqual(Path.GetFullPath(newer), response.SelectedFile);
    AssertEqual("newest_session", response.SessionId);
    AssertEqual(true, response.MessagesWritten > 0);
    return Task.CompletedTask;
});

Run("fallback raw ingest warning", () =>
{
    using var store = EmptyStore("m014_fallback_ingest");
    var dir = CreateSessionFixtureDir("m014_fallback");
    var file = Path.Combine(dir, "odd_session.yaml");
    File.WriteAllText(file, "not_messages: true\nstrange: m014 fallback distinctive phrase\n");
    var result = RunCli(
        "--db",
        store.DatabasePath,
        "ingest-aichat-session",
        "--source-identity",
        "session_fixture_runtime",
        "--file",
        file);
    AssertEqual(0, result.ExitCode);
    var response = JsonSerializer.Deserialize<TiroIngestStateResponse>(result.Stdout, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("fallback ingest output did not deserialize.");
    AssertEqual("ok", response.Status);
    AssertContains(response.Warnings, "message structure was not recognized");
    AssertEqual(1, response.MessagesWritten);
    AssertEqual(0, response.FactsWritten);
    AssertEqual(0, response.ChunksWritten);
    return Task.CompletedTask;
});

Run("max chars truncation warning", () =>
{
    using var store = EmptyStore("m014_truncate_ingest");
    var dir = CreateSessionFixtureDir("m014_truncate");
    var file = WriteSessionFixture(dir, "long_session.yaml", new string('x', 500), "2026-05-10T00:00:00Z");
    var result = RunCli(
        "--db",
        store.DatabasePath,
        "ingest-aichat-session",
        "--source-identity",
        "session_fixture_runtime",
        "--file",
        file,
        "--max-chars",
        "80");
    AssertEqual(0, result.ExitCode);
    var response = JsonSerializer.Deserialize<TiroIngestStateResponse>(result.Stdout, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("truncate ingest output did not deserialize.");
    AssertContains(response.Warnings, "truncated");
    return Task.CompletedTask;
});

Run("missing saved session fails clearly", () =>
{
    using var store = EmptyStore("m014_missing_file_cli");
    var result = RunCli(
        "--db",
        store.DatabasePath,
        "ingest-aichat-session",
        "--source-identity",
        "session_fixture_runtime",
        "--file",
        Path.Combine("/tmp", $"missing_{Guid.NewGuid():N}.yaml"));
    AssertEqual(true, result.ExitCode != 0);
    AssertEqual(true, result.Stderr.Contains("Saved AIChat session file not found", StringComparison.Ordinal));
    return Task.CompletedTask;
});

Console.WriteLine("Tiro planner tests passed.");

static PlannerConfig Config(PlannerMode mode, string? apiKey, bool keyFound) => new(
    mode,
    "GEMINI_API_KEY",
    apiKey,
    keyFound,
    keyFound ? "test" : null,
    "/tmp/missing/Tiro_v1.env",
    false,
    "gemini-2.5-flash",
    TimeSpan.FromMilliseconds(500));

static void Run(string name, Func<Task> test)
{
    try
    {
        test().GetAwaiter().GetResult();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        Environment.ExitCode = 1;
        throw;
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void AssertContains(IEnumerable<string> values, string expectedSubstring)
{
    if (!values.Any(value => value.Contains(expectedSubstring, StringComparison.Ordinal)))
    {
        throw new InvalidOperationException($"Expected debug output containing {expectedSubstring}.");
    }
}

static void AssertThrows(Action action)
{
    try
    {
        action();
    }
    catch (InvalidOperationException)
    {
        return;
    }
    catch (JsonException)
    {
        return;
    }
    catch (ArgumentException)
    {
        return;
    }
    catch (FileNotFoundException)
    {
        return;
    }

    throw new InvalidOperationException("Expected planner parsing to throw.");
}

static async Task AssertThrowsAsync(Func<Task> action)
{
    try
    {
        await action();
    }
    catch (InvalidOperationException)
    {
        return;
    }
    catch (ArgumentException)
    {
        return;
    }

    throw new InvalidOperationException("Expected operation to throw.");
}

static Task<TiroQueryResponse> QueryService(
    string databasePath,
    string query,
    int limit = 5,
    string? sourceId = null,
    string? documentId = null,
    int contextWindow = 0,
    string? sessionId = null,
    PlannerMode plannerMode = PlannerMode.Off,
    bool debugPlanner = false)
{
    return new TiroQueryService().QueryAsync(new TiroQueryRequest(
        databasePath,
        query,
        limit,
        sourceId,
        documentId,
        contextWindow,
        sessionId,
        plannerMode,
        debugPlanner));
}

static (int ExitCode, string Stdout, string Stderr) RunCli(params string[] arguments)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = "dotnet",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        WorkingDirectory = Directory.GetCurrentDirectory()
    };
    startInfo.ArgumentList.Add("run");
    startInfo.ArgumentList.Add("--no-build");
    startInfo.ArgumentList.Add("--project");
    startInfo.ArgumentList.Add("src/Tiro.Cli");
    startInfo.ArgumentList.Add("--");
    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start CLI process.");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, stdout, stderr);
}

static TiroStore BuildFixtureStore(string name)
{
    var path = Path.Combine("/tmp", $"tiro_m005_{name}_{Guid.NewGuid():N}.sqlite3");
    var store = TiroStore.Open(path);
    store.IngestChunks(Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures", "m002_chunks.jsonl"));
    store.IngestChunks(Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures", "m003_retrieval_chunks.jsonl"));
    return store;
}

static TiroStore EmptyStore(string name)
{
    var path = Path.Combine("/tmp", $"tiro_{name}_{Guid.NewGuid():N}.sqlite3");
    return TiroStore.Open(path);
}

static TiroStore BuildAlephStore(string name)
{
    var path = Path.Combine("/tmp", $"tiro_m022_{name}_{Guid.NewGuid():N}.sqlite3");
    var store = TiroStore.Open(path);
    store.IngestChunks(Path.Combine(Directory.GetCurrentDirectory(), "docs", "stack_smashing_tiro_chunks.jsonl"));
    return store;
}

static TiroStore BuildAlephProxyStore(string name)
{
    var store = BuildAlephStore(name);
    var response = new TiroProxyService().BuildCorpusProxies(new TiroProxyBuildRequest(
        store.DatabasePath,
        "corpus",
        "phrack49-file14-smashing-the-stack-for-fun-and-profit",
        null,
        Rebuild: false));
    if (response.Status != "ok" || response.ProxiesCreated == 0)
    {
        throw new InvalidOperationException("Aleph proxy fixture failed to build.");
    }

    return store;
}

static string CreateSessionFixtureDir(string name)
{
    var dir = Path.Combine("/tmp", $"tiro_{name}_{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    return dir;
}

static string WriteSessionFixture(string dir, string fileName, string phrase, string timestamp)
{
    var path = Path.Combine(dir, fileName);
    File.WriteAllText(
        path,
        $$"""
        model: gemini:gemini-2.5-flash
        role_name: session_fixture
        save_session: true
        messages:
        - role: user
          content: |-
            Please remember this fixture phrase: {{phrase}}
        - role: assistant
          content: |-
            Acknowledged at {{timestamp}}. Deterministic retrieval must remain explicit and inspectable.
        """);
    return path;
}

static TiroStore BuildSessionStore(string name)
{
    var store = BuildFixtureStore(name);
    store.CreateSession("session-alpha", DateTimeOffset.Parse("2026-05-10T00:00:00Z"));
    store.IngestMessage(
        "session-alpha",
        "user",
        "cli:user",
        "Remember that deployment token rotation is the current state for tonight.",
        DateTimeOffset.Parse("2026-05-10T00:01:00Z"));
    store.IngestMessage(
        "session-alpha",
        "assistant",
        "cli:assistant",
        "Acknowledged: provenance should stay explicit in the packet.",
        DateTimeOffset.Parse("2026-05-10T00:02:00Z"));
    store.IngestMessage(
        "session-alpha",
        "user",
        "cli:user",
        "The next checkpoint should mention deployment readiness and token rotation.",
        DateTimeOffset.Parse("2026-05-10T00:03:00Z"));
    return store;
}

static TiroStore BuildOperationalStore(string name)
{
    var store = BuildSessionStore(name);
    store.AddOperationalRecord(
        "decision",
        "Decision: deployment token rotation must preserve provenance.",
        "test:decision",
        "session-alpha",
        createdUtc: DateTimeOffset.Parse("2026-05-10T00:04:00Z"));
    store.AddOperationalRecord(
        "todo",
        "TODO: rotate deployment token before launch.",
        "test:todo",
        "session-alpha",
        createdUtc: DateTimeOffset.Parse("2026-05-10T00:05:00Z"));
    store.AddOperationalRecord(
        "unknown",
        "Unknown: deployment owner is not confirmed.",
        "test:unknown",
        "session-alpha",
        createdUtc: DateTimeOffset.Parse("2026-05-10T00:06:00Z"));
    store.AddOperationalRecord(
        "warning",
        "Warning: token rotation is blocked until owner confirms.",
        "test:warning",
        "session-alpha",
        createdUtc: DateTimeOffset.Parse("2026-05-10T00:07:00Z"));
    store.AddOperationalRecord(
        "decision",
        "Decision: global release policy is separate from this session.",
        "test:global",
        null,
        createdUtc: DateTimeOffset.Parse("2026-05-10T00:08:00Z"));
    return store;
}

static TiroStore BuildSemanticStore(string name)
{
    var store = EmptyStore(name);
    store.CreateSession("semantic-session", DateTimeOffset.Parse("2026-05-12T00:00:00Z"));
    store.IngestMessage(
        "semantic-session",
        "user",
        "test:semantic",
        "Saved session checkpointing stores AIChat conversation transcript content as session evidence for later retrieval.",
        DateTimeOffset.Parse("2026-05-12T00:01:00Z"));
    store.IngestMessage(
        "semantic-session",
        "assistant",
        "test:semantic",
        "Session memory works by ingesting saved sessions as evidence, not truth, with source identity and checkpoint provenance.",
        DateTimeOffset.Parse("2026-05-12T00:02:00Z"));
    store.AddOperationalRecord(
        "decision",
        "Decision: the write tool remains controlled through tiro_ingest_state for explicit state ingestion and write-side tooling.",
        "test:semantic",
        null,
        createdUtc: DateTimeOffset.Parse("2026-05-12T00:03:00Z"));
    store.AddOperationalRecord(
        "warning",
        "Warning: broad tools and unrestricted tool exposure must stay blocked; the runtime only gets narrow Tiro query and ingest tools.",
        "test:semantic",
        null,
        createdUtc: DateTimeOffset.Parse("2026-05-12T00:04:00Z"));
    return store;
}

static PlannerRunResult SemanticPlanner(
    string originalQuery,
    string searchQuery,
    string semanticIntent,
    IReadOnlyList<string> expandedQueries,
    IReadOnlyList<string> expandedTerms,
    IReadOnlyList<string> targetLanes)
{
    var config = Config(PlannerMode.On, "test-key", true);
    var advice = new PlannerAdvice(
        searchQuery,
        expandedTerms,
        targetLanes,
        Array.Empty<string>(),
        semanticIntent,
        expandedQueries,
        expandedTerms,
        targetLanes,
        Array.Empty<string>(),
        Array.Empty<string>(),
        null,
        expandedQueries.Count > 1 ? "multi_query_union" : "single_query",
        "medium",
        new[] { "Broad semantic planner expansion remains advisory." });
    return PlannerRunResult.Used(
        config,
        originalQuery,
        advice,
        PlannerHttpDiagnostics.ForRequest("https://example.invalid?key=<redacted>", "test-model", "query_parameter", TimeSpan.FromMilliseconds(500)),
        includeDebug: true);
}

static TiroStore BuildPolicyStore(string name)
{
    var path = Path.Combine("/tmp", $"tiro_m009_{name}_{Guid.NewGuid():N}.sqlite3");
    var store = TiroStore.Open(path);
    store.IngestChunks(Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures", "m009_policy_chunks.jsonl"));
    store.CreateSession("session-alpha", DateTimeOffset.Parse("2026-05-10T00:00:00Z"));
    store.IngestMessage(
        "session-alpha",
        "user",
        "cli:user",
        "Current deployment token owner is Eli.",
        DateTimeOffset.Parse("2026-05-10T00:01:00Z"));
    store.AddOperationalRecord(
        "decision",
        "Decision: deployment token owner is Morgan.",
        "test:policy",
        "session-alpha",
        createdUtc: DateTimeOffset.Parse("2026-05-10T00:02:00Z"));
    store.AddOperationalRecord(
        "todo",
        "TODO: confirm deployment token owner.",
        "test:policy",
        "session-alpha",
        createdUtc: DateTimeOffset.Parse("2026-05-10T00:03:00Z"));
    return store;
}

sealed class FakePlannerClient : IRetrievalPlannerClient
{
    private readonly PlannerAdvice _advice;

    public FakePlannerClient(PlannerAdvice advice)
    {
        _advice = advice;
    }

    public Task<PlannerClientResult> PlanAsync(string query, IReadOnlyList<string> deterministicTerms, CancellationToken cancellationToken)
    {
        var diagnostics = PlannerHttpDiagnostics.ForRequest(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=<redacted>",
            "gemini-2.5-flash",
            "query_parameter",
            TimeSpan.FromMilliseconds(500));
        return Task.FromResult(new PlannerClientResult(_advice, diagnostics));
    }
}

sealed class FailingPlannerClient : IRetrievalPlannerClient
{
    public Task<PlannerClientResult> PlanAsync(string query, IReadOnlyList<string> deterministicTerms, CancellationToken cancellationToken)
    {
        throw new PlannerClientException(
            "simulated planner failure",
            PlannerHttpDiagnostics.ForRequest(
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=<redacted>",
                "gemini-2.5-flash",
                "query_parameter",
                TimeSpan.FromMilliseconds(500)),
            new HttpRequestException("simulated planner failure"));
    }
}

sealed class InvalidOutputPlannerClient : IRetrievalPlannerClient
{
    public Task<PlannerClientResult> PlanAsync(string query, IReadOnlyList<string> deterministicTerms, CancellationToken cancellationToken)
    {
        var diagnostics = PlannerHttpDiagnostics.ForRequest(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=<redacted>",
            "gemini-2.5-flash",
            "query_parameter",
            TimeSpan.FromMilliseconds(500));
        var advice = PlannerAdvice.Parse("not json");
        return Task.FromResult(new PlannerClientResult(advice, diagnostics));
    }
}
