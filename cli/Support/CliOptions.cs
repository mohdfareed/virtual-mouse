using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using Hosting;
using Inputs.Sdl;

internal static class CliOptions
{
    internal static Option<int?> CreateCountOption(int defaultCount)
    {
        return CreatePositiveIntOption(
            "--count",
            $"Measured reports. Default: {defaultCount}.");
    }

    internal static Option<int?> CreateDurationMsOption(int defaultDurationMs)
    {
        return CreatePositiveIntOption(
            "--duration-ms",
            $"Button press duration. Default: {defaultDurationMs}.");
    }

    internal static Option<int?> CreateWaitMsOption(int defaultWaitMs)
    {
        return CreateNonNegativeIntOption(
            "--wait-ms",
            $"Wait for matching devices before failing. Default: {defaultWaitMs}.");
    }

    internal static Option<bool> CreatePauseOption()
    {
        return new Option<bool>("--pause")
        {
            Description = "Wait for Enter before exiting.",
        };
    }

    internal static Option<int?> CreateDeviceIndexOption(string name, string description)
    {
        return CreateNonNegativeIntOption(name, description);
    }

    internal static Option<SdlGamepadInputMode?> CreateSdlGamepadModeOption(string name, string description)
    {
        return new Option<SdlGamepadInputMode?>(name)
        {
            Description = description,
            CustomParser = result => ParseSdlGamepadInputMode(result, name),
        };
    }

    internal static Option<bool> CreateSdlPhysicalMotionOption(string name, string description)
    {
        return new Option<bool>(name)
        {
            Description = description,
        };
    }

    internal static Option<ForwardingRouteKind?> CreateRouteOption()
    {
        return new Option<ForwardingRouteKind?>("--route")
        {
            Description = "Route to enable for this client session. Omit to connect without enabling forwarding.",
        };
    }

    internal static Option<ForwardingRouteKind> CreateRequiredRouteOption()
    {
        return new Option<ForwardingRouteKind>("--route")
        {
            Description = "Route to control.",
            Required = true,
        };
    }

    internal static Option<string?> CreateSteamPathOption()
    {
        return new Option<string?>("--steam-path")
        {
            Description = "Steam install path. Defaults to SteamPath/SteamDir, registry, or common install paths.",
        };
    }

    internal static Option<uint?> CreateUserIdOption()
    {
        return new Option<uint?>("--user-id")
        {
            Description = "Steam userdata id for non-Steam shortcuts. Defaults to Steam's active user when available.",
        };
    }

    internal static Argument<uint> CreateAppIdArgument()
    {
        Argument<uint> argument = new("app-id")
        {
            Description = "Steam app id or non-Steam shortcut app id.",
        };
        argument.Validators.Add(result =>
        {
            if (result.GetValue(argument) == 0)
            {
                result.AddError("app-id must be greater than 0.");
            }
        });
        return argument;
    }

    internal static SdlGamepadOptions CreateSdlGamepadOptions(
        ParseResult parseResult,
        Option<int?> deviceIndexOption,
        Option<SdlGamepadInputMode?>? modeOption = null,
        Option<bool>? physicalMotionOption = null,
        Option<int?>? motionDeviceIndexOption = null,
        SdlGamepadInputMode defaultMode = SdlGamepadInputMode.Physical)
    {
        return new SdlGamepadOptions
        {
            DeviceIndex = parseResult.GetValue(deviceIndexOption) ?? 0,
            Mode = modeOption is null
                ? defaultMode
                : parseResult.GetValue(modeOption) ?? defaultMode,
            UsePhysicalMotion = physicalMotionOption is not null && parseResult.GetValue(physicalMotionOption),
            MotionDeviceIndex = motionDeviceIndexOption is null
                ? null
                : parseResult.GetValue(motionDeviceIndexOption),
        };
    }

    private static Option<int?> CreatePositiveIntOption(string name, string description)
    {
        Option<int?> option = new(name)
        {
            Description = description,
        };
        option.Validators.Add(result =>
        {
            int? value = result.GetValue(option);
            if (value.HasValue && value.Value <= 0)
            {
                result.AddError($"{name} must be greater than 0.");
            }
        });
        return option;
    }

    private static SdlGamepadInputMode? ParseSdlGamepadInputMode(ArgumentResult result, string optionName)
    {
        if (result.Tokens.Count == 0)
        {
            return null;
        }

        string value = result.Tokens[0].Value;
        string normalized = value.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return normalized switch
        {
            "PHYSICAL" => SdlGamepadInputMode.Physical,
            "STEAM" => SdlGamepadInputMode.Steam,
            _ => AddSdlGamepadModeError(result, optionName),
        };
    }

    private static SdlGamepadInputMode? AddSdlGamepadModeError(ArgumentResult result, string optionName)
    {
        result.AddError(
            $"{optionName} must be one of: physical, steam.");
        return null;
    }

    private static Option<int?> CreateNonNegativeIntOption(string name, string description)
    {
        Option<int?> option = new(name)
        {
            Description = description,
        };
        option.Validators.Add(result =>
        {
            int? value = result.GetValue(option);
            if (value.HasValue && value.Value < 0)
            {
                result.AddError($"{name} must be greater than or equal to 0.");
            }
        });
        return option;
    }
}
