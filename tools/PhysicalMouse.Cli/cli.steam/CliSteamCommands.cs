using System;
using System.CommandLine;
using System.Runtime.Versioning;
using PhysicalMouse;

internal static class CliSteamCommands
{
    // MARK: Commands
    // ========================================================================

    [SupportedOSPlatform("windows")]
    internal static Command CreateSteamCommand()
    {
        Command command = new("steam", "Steam input tools.");
        Option<bool> nullOption = new("--null")
        {
            Description = "Mirror Steam legacy mouse input back to the output mouse until Ctrl+C.",
            Required = true,
        };

        command.Options.Add(nullOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            _ = parseResult.GetValue(nullOption);

            _ = await CliConnection.ExecuteAsync(
                async (mouse, ct) =>
                {
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);
                    await Console.Out.WriteLineAsync("Steam nullifier running. Press Ctrl+C to stop.").ConfigureAwait(false);
                    SteamNullifier.Run(mouse, ct);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Helpers
    // ========================================================================

    internal static MouseReport Nullify(MouseReport report)
    {
        return new MouseReport(report.Buttons, -report.DeltaX, -report.DeltaY, -report.WheelDelta);
    }
}
