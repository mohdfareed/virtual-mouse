using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SteamInput;

/// <summary>Reads local Steam state and controls Steam Input through Steam URL commands.</summary>
/// <param name="openUrl">Steam URL opener. Defaults to the OS URL handler.</param>
public sealed class SteamInputClient(Func<Uri, CancellationToken, ValueTask>? openUrl = null)
{
    private const uint DesktopConfigAppId = 413080;

    private readonly Func<Uri, CancellationToken, ValueTask> _openUrl = openUrl ?? OpenSteamUrlAsync;

    /// <summary>Lists Steam and non-Steam games known locally.</summary>
    /// <param name="steamPath">Steam install path. When omitted, the local install is discovered.</param>
    /// <param name="steamUserId">Steam user id for non-Steam shortcuts. When omitted, the active user is used.</param>
    public static IReadOnlyList<SteamGame> ListGames(string? steamPath = null, uint? steamUserId = null)
    {
        string resolvedPath = ResolveSteamPath(steamPath);
        uint? resolvedUserId = steamUserId ?? SteamInstallLocator.FindActiveUserId();
        return new SteamGameCatalog(resolvedPath).ListGames(resolvedUserId);
    }

    /// <summary>Forces Steam Input to use an app's controller configuration.</summary>
    /// <param name="appId">Steam app id or non-Steam shortcut app id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask ForceAsync(uint appId, CancellationToken cancellationToken = default)
    {
        ValidateAppId(appId);
        return OpenAsync(CreateForceInputAppIdUri(appId), cancellationToken);
    }

    /// <summary>Forces Steam Input to use the desktop controller configuration.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask ForceDesktopAsync(CancellationToken cancellationToken = default)
    {
        return OpenAsync(CreateForceInputAppIdUri(DesktopConfigAppId), cancellationToken);
    }

    /// <summary>Clears Steam Input app id forcing.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        return OpenAsync(CreateForceInputAppIdUri(0), cancellationToken);
    }

    /// <summary>Opens Steam's controller configurator for an app.</summary>
    /// <param name="appId">Steam app id or non-Steam shortcut app id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask OpenControllerConfigAsync(uint appId, CancellationToken cancellationToken = default)
    {
        ValidateAppId(appId);
        return OpenAsync(CreateControllerConfigUri(appId), cancellationToken);
    }

    /// <summary>Opens Steam's desktop controller configurator.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask OpenDesktopControllerConfigAsync(CancellationToken cancellationToken = default)
    {
        return OpenAsync(CreateControllerConfigUri(DesktopConfigAppId), cancellationToken);
    }

    private ValueTask OpenAsync(Uri url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _openUrl(url, cancellationToken);
    }

    private static Uri CreateForceInputAppIdUri(uint appId)
    {
        return new Uri($"steam://forceinputappid/{appId.ToString(CultureInfo.InvariantCulture)}");
    }

    private static Uri CreateControllerConfigUri(uint appId)
    {
        return new Uri($"steam://controllerconfig/{appId.ToString(CultureInfo.InvariantCulture)}");
    }

    private static void ValidateAppId(uint appId)
    {
        if (appId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appId), "Steam app id must be non-zero.");
        }
    }

    private static string ResolveSteamPath(string? steamPath)
    {
        string? resolvedPath = string.IsNullOrWhiteSpace(steamPath)
            ? SteamInstallLocator.FindSteamPath()
            : Path.GetFullPath(steamPath);

        return string.IsNullOrWhiteSpace(resolvedPath) || !Directory.Exists(resolvedPath)
            ? throw new InvalidOperationException("Could not find Steam. Pass a Steam path.")
            : resolvedPath;
    }

    private static ValueTask OpenSteamUrlAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(url.Scheme, "steam", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only steam:// URLs are supported.", nameof(url));
        }

        ProcessStartInfo startInfo = OperatingSystem.IsLinux()
            ? new ProcessStartInfo("xdg-open", url.AbsoluteUri)
            : new ProcessStartInfo
            {
                FileName = url.AbsoluteUri,
                UseShellExecute = true,
            };

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not open the Steam URL.");
        return ValueTask.CompletedTask;
    }
}
