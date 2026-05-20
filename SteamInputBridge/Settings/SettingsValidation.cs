using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SteamInputBridge.Settings.Profiles;

namespace SteamInputBridge.Settings;

internal static class SettingsValidation
{
    public static void Validate(SteamInputBridgeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        List<string> failures = [];
        ValidateViiper(settings.Viiper, failures);
        ValidateHidHide(settings.HidHide, failures);
        ValidateShortcuts(settings.Shortcuts, failures);
        ValidateProfiles(settings.Games, failures);

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        }
    }

    private static void ValidateViiper(ViiperSettings settings, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            failures.Add("viiper:host is required.");
        }

        if (settings.Port is < 1 or > 65_535)
        {
            failures.Add("viiper:port must be between 1 and 65535.");
        }
    }

    private static void ValidateHidHide(HidHideSettings settings, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(settings.CliPath))
        {
            failures.Add("hidhide:cliPath is required.");
        }
    }

    private static void ValidateShortcuts(
        Collection<ShortcutEntry> shortcuts,
        List<string> failures)
    {
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < shortcuts.Count; i++)
        {
            ShortcutEntry shortcut = shortcuts[i];
            string prefix = $"shortcuts:{i}";
            if (string.IsNullOrWhiteSpace(shortcut.Keys))
            {
                failures.Add($"{prefix}:keys is required.");
            }
            else if (!keys.Add(shortcut.Keys.Trim()))
            {
                failures.Add($"{prefix}:keys duplicates another shortcut.");
            }

            if (!shortcut.Target.HasValue)
            {
                failures.Add($"{prefix}:target is required.");
            }

            if (!shortcut.Value.HasValue)
            {
                failures.Add($"{prefix}:value is required.");
            }
        }
    }

    private static void ValidateProfiles(
        IReadOnlyDictionary<string, GameProfile> profiles,
        List<string> failures)
    {
        foreach ((string profileId, GameProfile profile) in profiles)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                failures.Add("games contains an empty profile id.");
                continue;
            }

            bool hasExecutable = !string.IsNullOrWhiteSpace(profile.Executable);
            bool hasReceivers = profile.ReceiverProcesses.Any(static receiver =>
                !string.IsNullOrWhiteSpace(receiver));
            if (!hasExecutable && !hasReceivers)
            {
                failures.Add(
                    $"games:{profileId}:receiverProcesses is required when executable is missing.");
            }
        }
    }
}
