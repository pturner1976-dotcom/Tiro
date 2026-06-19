namespace Tiro.Cli;

public sealed record CliOptions(
    string DatabasePath,
    string Command,
    IReadOnlyList<string> Arguments,
    int Limit,
    PlannerMode PlannerMode,
    bool DebugPlanner,
    RetrievalFilters Filters,
    int ContextWindow,
    string? SessionId)
{
    public static CliOptions Parse(string[] args)
    {
        var databasePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "tiro.sqlite3");
        var limit = 3;
        var plannerMode = PlannerMode.Auto;
        var debugPlanner = false;
        string? sourceId = null;
        string? documentId = null;
        string? sessionId = null;
        var contextWindow = 0;
        var remaining = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "--db" or "--database")
            {
                if (index + 1 >= args.Length)
                {
                    throw new InvalidOperationException("--db requires a path.");
                }
                databasePath = args[++index];
                continue;
            }

            if (arg == "--limit")
            {
                if (index + 1 >= args.Length || !int.TryParse(args[++index], out limit) || limit < 1)
                {
                    throw new InvalidOperationException("--limit requires a positive integer.");
                }
                continue;
            }

            if (arg == "--planner")
            {
                if (index + 1 >= args.Length || !TryParsePlannerMode(args[++index], out plannerMode))
                {
                    throw new InvalidOperationException("--planner requires one of: on, off, auto.");
                }
                continue;
            }

            if (arg == "--debug-planner")
            {
                debugPlanner = true;
                continue;
            }

            if (arg == "--source-id")
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new InvalidOperationException("--source-id requires a value.");
                }
                sourceId = args[++index];
                continue;
            }

            if (arg == "--document-id")
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new InvalidOperationException("--document-id requires a value.");
                }
                documentId = args[++index];
                continue;
            }

            if (arg == "--context-window")
            {
                if (index + 1 >= args.Length || !int.TryParse(args[++index], out contextWindow) || contextWindow < 0 || contextWindow > 2)
                {
                    throw new InvalidOperationException("--context-window requires an integer from 0 to 2.");
                }
                continue;
            }

            if (arg == "--session-id")
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new InvalidOperationException("--session-id requires a value.");
                }
                sessionId = args[++index];
                continue;
            }

            remaining.Add(arg);
        }

        var filters = new RetrievalFilters(sourceId, documentId);
        if (remaining.Count == 0)
        {
            return new CliOptions(databasePath, "help", Array.Empty<string>(), limit, plannerMode, debugPlanner, filters, contextWindow, sessionId);
        }

        return new CliOptions(databasePath, remaining[0], remaining.Skip(1).ToArray(), limit, plannerMode, debugPlanner, filters, contextWindow, sessionId);
    }

    private static bool TryParsePlannerMode(string value, out PlannerMode mode)
    {
        mode = value.ToLowerInvariant() switch
        {
            "auto" => PlannerMode.Auto,
            "on" => PlannerMode.On,
            "off" => PlannerMode.Off,
            _ => PlannerMode.Auto
        };

        return value.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("off", StringComparison.OrdinalIgnoreCase);
    }
}

public enum PlannerMode
{
    Auto,
    On,
    Off
}
