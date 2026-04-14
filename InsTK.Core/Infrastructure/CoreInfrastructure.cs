using System.Text.Json;
using System.Text.RegularExpressions;

namespace InsTK.Core;

internal static class InsTkDefaults
{
    public const string ArtifactSchemaVersion = "1.0";
    public const string ScraperName = "InsTK";
}

internal static class CommandValueParser
{
    public static int? ParseOptionalInt(string? value, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"Option --{optionName} must be a positive integer.");
        }

        return parsed;
    }
}

internal static class CorePaths
{
    public static string ResolvePath(string path)
        => Path.GetFullPath(path, Directory.GetCurrentDirectory());

    public static string? ResolveOptionalFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return File.Exists(path) ? path : null;
    }

    public static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = Regex.Replace(value, @"[^A-Za-z0-9._-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }
}

internal static class JsonFileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static async Task WriteAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Options));
    }

    public static async Task<T> ReadAsync<T>(string path)
        => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), Options)
            ?? throw new InvalidOperationException($"Failed to deserialize JSON from {path}");
}
