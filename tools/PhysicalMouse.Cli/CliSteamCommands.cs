using System;
using System.CommandLine;
using System.Runtime.Versioning;
using System.Threading;
using PhysicalMouse;
using PhysicalMouse.Viiper;
using VirtualMouse;
using VirtualMouse.RawInput;

internal enum SteamMouseMode
{
    Nullify,
    Forward,
}

internal static class CliSteamCommands
{
    // MARK: Commands
    // ========================================================================

    [SupportedOSPlatform("windows")]
    internal static Command CreateSteamCommand()
    {
        return CreateBridgeCommand(
            "steam",
            "Forward Steam mouse input to the output mouse.",
            SteamMouseMode.Forward);
    }

    [SupportedOSPlatform("windows")]
    internal static Command CreateNullifyCommand()
    {
        return CreateBridgeCommand(
            "nullify",
            "Send opposite Steam mouse movement to the output mouse.",
            SteamMouseMode.Nullify);
    }

    // MARK: Helpers
    // ========================================================================

    internal static MouseReport Nullify(MouseReport report)
    {
        return MouseReportTransforms.NullifyMovement(report);
    }

    internal static MouseReport ApplyMode(MouseReport report, SteamMouseMode mode)
    {
        return mode switch
        {
            SteamMouseMode.Nullify => Nullify(report),
            SteamMouseMode.Forward => report,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    [SupportedOSPlatform("windows")]
    private static Command CreateBridgeCommand(string name, string description, SteamMouseMode mode)
    {
        Command command = new(name, description);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            _ = parseResult;

            _ = await CliConnection.ExecuteAsync(
                async (mouse, ct) =>
                {
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"{name}: running. Ctrl+C to stop.").ConfigureAwait(false);
                    SteamNullifier.Run(mouse, mode, ct);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }
}

[SupportedOSPlatform("windows")]
internal static class SteamNullifier
{
    // MARK: Bridge
    // ========================================================================

    public static void Run(ViiperPhysicalMouse mouse, SteamMouseMode mode, CancellationToken cancellationToken)
    {
        using RawInputVirtualMouse input = RawInputVirtualMouse
            .ConnectAsync(cancellationToken)
            .GetAwaiter()
            .GetResult();

        if (mode == SteamMouseMode.Forward)
        {
            input.RunTo(mouse, cancellationToken);
            return;
        }

        input.RunTo(mouse, report => CliSteamCommands.ApplyMode(report, mode), cancellationToken);
    }
}
