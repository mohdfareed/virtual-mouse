using System;
using System.CommandLine;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
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

    internal static bool TryCreateOutput(
        in VirtualMouseInput source,
        SteamMouseMode mode,
        out MouseReport output)
    {
        output = MouseReport.Empty;
        if (IsOwnedDeviceName(source.DeviceName))
        {
            return false;
        }

        output = ApplyMode(source.Report, mode);
        return !output.IsEmpty;
    }

    internal static bool IsOwnedDeviceName(string deviceName)
    {
        return deviceName.Contains(OwnedDeviceFragment, StringComparison.OrdinalIgnoreCase);
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

    private const string OwnedDeviceFragment = "VID_6969&PID_5050";
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

        input.Run(HandleInput, cancellationToken);

        void HandleInput(in VirtualMouseInput source)
        {
            if (CliSteamCommands.TryCreateOutput(in source, mode, out MouseReport output))
            {
                SendSynchronously(mouse, output, cancellationToken);
            }
        }
    }

    // MARK: Helpers
    // ========================================================================

    private static void SendSynchronously(ViiperPhysicalMouse mouse, MouseReport report, CancellationToken cancellationToken)
    {
        ValueTask sendTask = mouse.SendAsync(report, cancellationToken);
        if (sendTask.IsCompleted)
        {
            sendTask.GetAwaiter().GetResult();
            return;
        }

        sendTask.AsTask().GetAwaiter().GetResult();
    }
}
