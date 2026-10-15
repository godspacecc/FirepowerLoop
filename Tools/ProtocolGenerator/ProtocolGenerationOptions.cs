namespace ProtocolGeneratorTool;

internal sealed record ProtocolGenerationOptions(
    string SourceDirectory,
    string ClientOutputDirectory,
    string ServerOutputDirectory)
{
    public static bool TryParse(
        string[] args,
        out ProtocolGenerationOptions options,
        out string error)
    {
        options = null!;
        error = string.Empty;

        Dictionary<string, string> values = new(StringComparer.Ordinal)
        {
            ["--source"] = string.Empty,
            ["--client-output"] = string.Empty,
            ["--server-output"] = string.Empty
        };

        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string name = args[i];
            if (!values.ContainsKey(name))
            {
                error = $"Unknown argument: {name}";
                return false;
            }

            if (!seen.Add(name))
            {
                error = $"Duplicate argument: {name}";
                return false;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Missing value for argument: {name}";
                return false;
            }

            values[name] = args[++i];
        }

        foreach ((string name, string value) in values)
        {
            if (!seen.Contains(name) || string.IsNullOrWhiteSpace(value))
            {
                error = $"Missing required argument: {name}";
                return false;
            }
        }

        try
        {
            string source = Path.GetFullPath(values["--source"]);
            string client = Path.GetFullPath(values["--client-output"]);
            string server = Path.GetFullPath(values["--server-output"]);

            HashSet<string> outputDirectories = new(StringComparer.OrdinalIgnoreCase)
            {
                TrimDirectory(client)
            };

            if (!outputDirectories.Add(TrimDirectory(server)))
            {
                error = "Output directories must be distinct.";
                return false;
            }

            options = new ProtocolGenerationOptions(source, client, server);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Invalid path: {e.Message}";
            return false;
        }
    }

    private static string TrimDirectory(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
