using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Settings;

// MARK: Models
// ============================================================================

/// <summary>Resolved settings file path.</summary>
public sealed record SettingsFile(string Path);

/// <summary>Application-owned settings root.</summary>
public sealed class VirtualMouseSettings
{
    /// <summary>Root section for app-owned settings.</summary>
    public const string SectionName = "VirtualMouse";

    /// <summary>Application logging settings.</summary>
    public LoggingSettings Logging { get; set; } = new();

    /// <summary>VIIPER output settings.</summary>
    public ViiperSettings Viiper { get; set; } = new();

    /// <summary>Steam integration settings.</summary>
    public SteamSettings Steam { get; set; } = new();

    /// <summary>HidHide device firewall settings.</summary>
    public HidHideSettings HidHide { get; set; } = new();

    /// <summary>Configured game profiles by profile id.</summary>
    public Dictionary<string, GameProfile> Games { get; } = [];
}

/// <summary>Steam integration settings.</summary>
public sealed class SteamSettings
{
    /// <summary>Configuration section name for Steam settings.</summary>
    public const string SectionName = VirtualMouseSettings.SectionName + ":Steam";

    /// <summary>Default Steam ROM Manager manifest export path.</summary>
    public string? SrmExportPath { get; set; }
}

/// <summary>HidHide device firewall settings.</summary>
public sealed class HidHideSettings
{
    /// <summary>Configuration section name for HidHide settings.</summary>
    public const string SectionName = VirtualMouseSettings.SectionName + ":HidHide";

    /// <summary>HidHide command-line executable path.</summary>
    public string CliPath { get; set; } =
        @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe";
}

/// <summary>VIIPER output settings.</summary>
public sealed class ViiperSettings
{
    /// <summary>Configuration section name for VIIPER output settings.</summary>
    public const string SectionName = VirtualMouseSettings.SectionName + ":Viiper";

    /// <summary>VIIPER server host.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>VIIPER server port.</summary>
    public int Port { get; set; } = 3242;
}

/// <summary>Application logging settings.</summary>
public sealed class LoggingSettings
{
    /// <summary>Configuration section name for logging settings.</summary>
    public const string SectionName = VirtualMouseSettings.SectionName + ":Logging";

    /// <summary>Minimum log level.</summary>
    public LogLevel Level { get; set; } = LogLevel.Information;

    /// <summary>Optional log directory.</summary>
    public string? LogDirectory { get; set; }
}
