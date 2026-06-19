using System.Text;
using System.Text.RegularExpressions;

namespace Tiro.Cli;

public static partial class AichatSessionDiscovery
{
    public static AichatSessionInspection Inspect(string? sessionsDirectory = null)
    {
        var directory = ResolveSessionsDirectory(sessionsDirectory);
        var warnings = new List<string>();
        if (!Directory.Exists(directory))
        {
            warnings.Add($"AIChat sessions directory was not found: {directory}");
            return new AichatSessionInspection(directory, 0, null, Array.Empty<AichatSessionCandidate>(), warnings);
        }

        var candidates = Directory.EnumerateFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly)
            .Where(path => !IsBackup(path))
            .Select(BuildCandidate)
            .OrderByDescending(candidate => candidate.ModifiedUtc)
            .ThenBy(candidate => candidate.File, StringComparer.Ordinal)
            .ToArray();

        return new AichatSessionInspection(
            directory,
            candidates.Length,
            candidates.FirstOrDefault()?.File,
            candidates,
            warnings);
    }

    public static (string File, IReadOnlyList<string> Warnings) SelectSessionFile(string? explicitFile, string? sessionsDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitFile))
        {
            return (Path.GetFullPath(explicitFile), Array.Empty<string>());
        }

        var inspection = Inspect(sessionsDirectory);
        if (string.IsNullOrWhiteSpace(inspection.NewestFile))
        {
            throw new FileNotFoundException($"No saved AIChat session YAML files were found in {inspection.SessionsDir}.");
        }

        return (inspection.NewestFile, inspection.Warnings);
    }

    public static string DeriveSessionId(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        name = name.Replace(' ', '_');
        var builder = new StringBuilder();
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_')
            {
                builder.Append(ch);
            }
            else
            {
                builder.Append('_');
            }
        }

        return CollapseUnderscores().Replace(builder.ToString(), "_").Trim('_');
    }

    private static AichatSessionCandidate BuildCandidate(string path)
    {
        var info = new FileInfo(path);
        return new AichatSessionCandidate(
            info.FullName,
            info.Length,
            info.LastWriteTimeUtc,
            DeriveSessionId(info.FullName));
    }

    private static string ResolveSessionsDirectory(string? sessionsDirectory)
    {
        if (!string.IsNullOrWhiteSpace(sessionsDirectory))
        {
            return Path.GetFullPath(sessionsDirectory);
        }

        var configured = Environment.GetEnvironmentVariable("AICHAT_SESSIONS_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var configDir = Environment.GetEnvironmentVariable("AICHAT_CONFIG_DIR");
        return Path.Combine(
            string.IsNullOrWhiteSpace(configDir)
                ? "/home/SiliconMagician/.config/aichat"
                : Path.GetFullPath(configDir),
            "sessions");
    }

    private static bool IsBackup(string path)
    {
        var name = Path.GetFileName(path);
        return name.Contains(".bak.", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".bak.yaml", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("~", StringComparison.Ordinal);
    }

    [GeneratedRegex("_+")]
    private static partial Regex CollapseUnderscores();
}

public sealed record ParsedAichatSessionMessage(string Direction, string Text);

public sealed record ParsedAichatSession(
    IReadOnlyList<ParsedAichatSessionMessage> Messages,
    IReadOnlyList<string> Warnings);

public static class AichatSessionParser
{
    public static ParsedAichatSession Parse(string filePath, string content, DateTimeOffset importedUtc)
    {
        var warnings = new List<string>();
        var messages = TryParseMessages(content);
        if (messages.Count == 0)
        {
            warnings.Add("AIChat session YAML message structure was not recognized; ingested bounded raw session evidence as one system message.");
            messages.Add(new ParsedAichatSessionMessage("system", BuildRawArtifact(filePath, content, importedUtc)));
        }
        else
        {
            warnings.Add($"AIChat session YAML parsed into {messages.Count} session messages.");
            messages[0] = messages[0] with
            {
                Text = BuildMetadataHeader(filePath, content, importedUtc) + "\n\n" + messages[0].Text
            };
        }

        return new ParsedAichatSession(messages, warnings);
    }

    private static List<ParsedAichatSessionMessage> TryParseMessages(string content)
    {
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var results = new List<ParsedAichatSessionMessage>();
        string? role = null;
        var body = new StringBuilder();
        var inContentBlock = false;
        var contentIndent = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.StartsWith("- role:", StringComparison.Ordinal))
            {
                Flush();
                role = NormalizeDirection(trimmed["- role:".Length..].Trim().Trim('"', '\''));
                inContentBlock = false;
                continue;
            }

            if (role is null)
            {
                continue;
            }

            if (trimmed.StartsWith("content:", StringComparison.Ordinal))
            {
                inContentBlock = true;
                contentIndent = CountIndent(line);
                var inline = trimmed["content:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(inline) && inline is not "|-" and not "|" and not ">-" and not ">")
                {
                    body.AppendLine(inline.Trim('"', '\''));
                }
                continue;
            }

            if (inContentBlock)
            {
                var indent = CountIndent(line);
                if (trimmed.Length > 0 && indent <= contentIndent && trimmed.Contains(':', StringComparison.Ordinal))
                {
                    inContentBlock = false;
                    continue;
                }

                body.AppendLine(DedentContentLine(line, contentIndent + 2));
            }
        }

        Flush();
        return results;

        void Flush()
        {
            if (role is null)
            {
                return;
            }

            var text = body.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                results.Add(new ParsedAichatSessionMessage(role, text));
            }

            body.Clear();
        }
    }

    private static string BuildMetadataHeader(string filePath, string content, DateTimeOffset importedUtc)
    {
        return string.Join('\n', new[]
        {
            $"Saved AIChat session artifact: {Path.GetFileName(filePath)}",
            $"Source path: {Path.GetFullPath(filePath)}",
            $"Imported UTC: {importedUtc:O}",
            $"Session metadata: {ExtractMetadata(content)}"
        });
    }

    private static string BuildRawArtifact(string filePath, string content, DateTimeOffset importedUtc)
    {
        return $"{BuildMetadataHeader(filePath, content, importedUtc)}\n\n{content}";
    }

    private static string ExtractMetadata(string content)
    {
        var wanted = new[] { "model", "role_name", "save_session" };
        var values = new List<string>();
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            foreach (var key in wanted)
            {
                if (trimmed.StartsWith($"{key}:", StringComparison.Ordinal))
                {
                    values.Add(trimmed);
                }
            }
        }

        return values.Count == 0 ? "(none parsed)" : string.Join("; ", values);
    }

    private static string NormalizeDirection(string role)
    {
        return role.ToLowerInvariant() switch
        {
            "user" => "user",
            "assistant" => "assistant",
            "system" => "system",
            _ => "system"
        };
    }

    private static int CountIndent(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

    private static string DedentContentLine(string line, int contentIndent)
    {
        if (line.Length >= contentIndent)
        {
            return line[contentIndent..];
        }

        return line.TrimStart();
    }
}
