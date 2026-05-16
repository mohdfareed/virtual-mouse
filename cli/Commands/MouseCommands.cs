using System;
using System.CommandLine;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Cli.Tools;
using Hosting;
using Outputs;

internal static class MouseCommands
{
    // MARK: Commands
    // ========================================================================

    [SupportedOSPlatform("windows")]
    internal static Command CreateMouseCommand()
    {
        Command command = new("mouse", "Forward Raw Input mouse reports.");
        command.Subcommands.Add(CreateRunCommand());
        command.Subcommands.Add(CreateForwardCommand());
        command.Subcommands.Add(CreateNullifyCommand());
        return command;
    }

    [SupportedOSPlatform("windows")]
    internal static Command CreateLegacyNullifyCommand()
    {
        return CreateBridgeCommand(
            "nullify",
            "Send opposite Steam mouse movement to the output mouse.",
            MouseNullifier.RunRawInputToAsync);
    }

    [SupportedOSPlatform("windows")]
    private static Command CreateRunCommand()
    {
        return CreateBridgeCommand(
            "run",
            "Start forwarding mouse input.",
            MouseForwarding.RunRawInputToAsync);
    }

    [SupportedOSPlatform("windows")]
    private static Command CreateForwardCommand()
    {
        return CreateBridgeCommand(
            "forward",
            "Forward Raw Input mouse reports to the output mouse.",
            MouseForwarding.RunRawInputToAsync);
    }

    [SupportedOSPlatform("windows")]
    private static Command CreateNullifyCommand()
    {
        return CreateBridgeCommand(
            "nullify",
            "Send opposite Steam mouse movement to the output mouse.",
            MouseNullifier.RunRawInputToAsync);
    }

    [SupportedOSPlatform("windows")]
    private static Command CreateBridgeCommand(
        string name,
        string description,
        Func<IMouseOutput, CancellationToken, Task> runAsync)
    {
        Command command = new(name, description);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            _ = parseResult;

            _ = await ViiperConnection.ExecuteMouseAsync(
                async (mouse, ct) =>
                {
                    await ViiperConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"mouse {name}: running. Ctrl+C to stop.").ConfigureAwait(false);
                    await runAsync(mouse, ct).ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }
}
