using System;
using System.CommandLine;
using System.Runtime.Versioning;
using PhysicalMouse;

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
        return new MouseReport(MouseButtons.None, -report.DeltaX, -report.DeltaY, 0);
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
