namespace Tiro.Cli;

public sealed record PlannerConfig(
    PlannerMode Mode,
    string KeyName,
    string? ApiKey,
    bool KeyFound,
    string? KeySource,
    string EnvFilePath,
    bool EnvFileFound,
    string Model,
    TimeSpan Timeout)
{
    public static PlannerConfig Load(PlannerMode mode)
    {
        const string keyName = "GEMINI_API_KEY";
        const string modelName = "GEMINI_PLANNER_MODEL";
        const string defaultModel = "gemini-2.5-flash";
        var envFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".env",
            "tiro.env");

        var processValue = Environment.GetEnvironmentVariable(keyName);
        var processModel = Environment.GetEnvironmentVariable(modelName);
        var envFileFound = File.Exists(envFilePath);
        string? fileValue = null;
        string? fileModel = null;
        if (envFileFound)
        {
            fileValue = LoadEnvFileValue(envFilePath, keyName);
            fileModel = LoadEnvFileValue(envFilePath, modelName);
        }

        var apiKey = string.IsNullOrWhiteSpace(processValue) ? fileValue : processValue;
        var model = string.IsNullOrWhiteSpace(processModel)
            ? string.IsNullOrWhiteSpace(fileModel) ? defaultModel : fileModel
            : processModel;
        var keySource = !string.IsNullOrWhiteSpace(processValue)
            ? "process"
            : !string.IsNullOrWhiteSpace(fileValue)
                ? "env_file"
                : null;

        return new PlannerConfig(
            mode,
            keyName,
            string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            !string.IsNullOrWhiteSpace(apiKey),
            keySource,
            envFilePath,
            envFileFound,
            model.Trim(),
            TimeSpan.FromSeconds(8));
    }

    private static string? LoadEnvFileValue(string path, string keyName)
    {
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = trimmed[..separator].Trim();
            if (!string.Equals(name, keyName, StringComparison.Ordinal))
            {
                continue;
            }

            var value = trimmed[(separator + 1)..].Trim();
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            return value;
        }

        return null;
    }
}
