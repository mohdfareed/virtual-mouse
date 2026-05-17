using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Profiles;

/// <summary>Virtual controller output selected by a profile.</summary>
public enum ControllerOutputKind
{
    /// <summary>No virtual controller output.</summary>
    None,

    /// <summary>Xbox 360 virtual controller output.</summary>
    Xbox360,

    /// <summary>DualShock 4 virtual controller output.</summary>
    Ds4,
}

/// <summary>Mouse output selected by a profile.</summary>
public enum MouseOutputKind
{
    /// <summary>No mouse output.</summary>
    None,

    /// <summary>VIIPER virtual mouse output.</summary>
    Viiper,
}

/// <summary>One game profile.</summary>
public sealed record GameProfile
{
    /// <summary>Display title.</summary>
    public string? Title { get; init; }

    /// <summary>Executable path launched by the client.</summary>
    public string? Executable { get; init; }

    /// <summary>Command-line arguments.</summary>
    public string? Arguments { get; init; }

    /// <summary>Working directory. Defaults to executable directory.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Receiver process names used for foreground gating.</summary>
    public IReadOnlyList<string> ReceiverProcesses { get; init; } = [];

    /// <summary>Virtual controller output.</summary>
    public ControllerOutputKind ControllerOutput { get; init; } = ControllerOutputKind.Xbox360;

    /// <summary>Mouse output.</summary>
    public MouseOutputKind MouseOutput { get; init; } = MouseOutputKind.Viiper;
}

/// <summary>Steam ROM Manager export settings.</summary>
public sealed record SteamRomManagerSettings
{
    /// <summary>Manifest path written by SRM export.</summary>
    public string? ManifestPath { get; init; }
}

/// <summary>Resolved profile ready for runtime use.</summary>
public sealed record ResolvedGameProfile(
    string Id,
    string Title,
    string Executable,
    string Arguments,
    string WorkingDirectory,
    IReadOnlyList<string> ReceiverProcesses,
    ControllerOutputKind ControllerOutput,
    MouseOutputKind MouseOutput);

/// <summary>Profile settings helpers.</summary>
public static class ProfileCatalog
{
    /// <summary>Gets the executable-local appsettings path.</summary>
    public static string GetDefaultSettingsPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }

    /// <summary>Resolves one configured profile.</summary>
    public static ResolvedGameProfile Resolve(
        IReadOnlyDictionary<string, GameProfile> profiles,
        string profileId)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        if (!profiles.TryGetValue(profileId, out GameProfile? profile))
        {
            throw new KeyNotFoundException($"Profile \"{profileId}\" was not found.");
        }

        string executable = NormalizePath(profile.Executable);
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException($"Profile \"{profileId}\" has no executable.");
        }

        string workingDirectory = NormalizePath(profile.WorkingDirectory);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory;
        }

        string[] receivers = GetReceiverProcesses(profile, executable);
        string title = string.IsNullOrWhiteSpace(profile.Title)
            ? ToTitle(profileId)
            : profile.Title.Trim();

        return new ResolvedGameProfile(
            profileId,
            title,
            executable,
            profile.Arguments ?? string.Empty,
            workingDirectory,
            receivers,
            profile.ControllerOutput,
            profile.MouseOutput);
    }

    private static string[] GetReceiverProcesses(GameProfile profile, string executable)
    {
        List<string> receivers = [];
        foreach (string receiver in profile.ReceiverProcesses)
        {
            if (!string.IsNullOrWhiteSpace(receiver))
            {
                receivers.Add(Path.GetFileName(receiver.Trim()));
            }
        }

        if (receivers.Count == 0)
        {
            string executableName = Path.GetFileName(executable);
            if (!string.IsNullOrWhiteSpace(executableName))
            {
                receivers.Add(executableName);
            }
        }

        return [.. receivers];
    }

    private static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Environment.ExpandEnvironmentVariables(path.Trim());
    }

    private static string ToTitle(string profileId)
    {
        string spaced = profileId.Replace('-', ' ').Replace('_', ' ');
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(spaced);
    }
}
