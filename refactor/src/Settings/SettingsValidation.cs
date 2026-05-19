using System;
using System.Collections.Generic;
using System.Linq;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Settings;

internal static class SettingsValidation
{
    public static void Validate(VirtualMouseSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        List<string> failures = [];
        ValidateViiper(settings.Viiper, failures);
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
