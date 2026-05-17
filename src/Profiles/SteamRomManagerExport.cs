using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Profiles;

/// <summary>Creates Steam ROM Manager manifests for profiles.</summary>
public static class SteamRomManagerExport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>Creates manifest JSON for profile-targeted Steam shortcuts.</summary>
    public static string CreateJson(
        IReadOnlyDictionary<string, GameProfile> profiles,
        string executablePath)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        string startIn = Path.GetDirectoryName(executablePath) ?? string.Empty;
        List<SteamRomManagerEntry> entries = [];
        foreach (KeyValuePair<string, GameProfile> item in profiles)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
            {
                continue;
            }

            string title = string.IsNullOrWhiteSpace(item.Value.Title)
                ? ToTitle(item.Key)
                : item.Value.Title.Trim();
            entries.Add(new SteamRomManagerEntry(
                title,
                executablePath,
                startIn,
                $"client run {QuoteArgument(item.Key)}",
                AppendArgsToExecutable: false));
        }

        return JsonSerializer.Serialize(entries, JsonOptions);
    }

    /// <summary>Writes manifest JSON for profile-targeted Steam shortcuts.</summary>
    public static void Write(
        IReadOnlyDictionary<string, GameProfile> profiles,
        string executablePath,
        string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        string? directory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        File.WriteAllText(manifestPath, CreateJson(profiles, executablePath));
    }

    private static string QuoteArgument(string value)
    {
        return !value.Contains(' ', StringComparison.Ordinal) && !value.Contains('"', StringComparison.Ordinal)
            ? value
            : $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string ToTitle(string profileId)
    {
        string spaced = profileId.Replace('-', ' ').Replace('_', ' ');
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(spaced);
    }

    private sealed record SteamRomManagerEntry(
        string Title,
        string Target,
        string StartIn,
        string LaunchOptions,
        bool AppendArgsToExecutable);
}
