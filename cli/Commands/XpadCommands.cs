using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Cli.Tools;
using Inputs;
using Inputs.Sdl;
using Outputs;

internal static class XpadCommands
{
    private static readonly TimeSpan ProbeRetryInterval = TimeSpan.FromMilliseconds(250);

    internal static string DisplayButtons(GamepadButtons buttons)
    {
        return buttons == GamepadButtons.None
            ? "none"
            : buttons.ToString();
    }

    internal static string DisplayMotion(GamepadMotion motion)
    {
        return $"gyro={DisplayVector(motion.HasGyro, motion.GyroX, motion.GyroY, motion.GyroZ)} " +
            $"accel={DisplayVector(motion.HasAccelerometer, motion.AccelX, motion.AccelY, motion.AccelZ)}";
    }

    // MARK: Command Helpers
    // ========================================================================

    internal static Command CreateProbeCommand()
    {
        Command command = new("probe", "List SDL gamepads.");
        Option<int?> waitMsOption = CliOptions.CreateWaitMsOption(0);
        Option<bool> pauseOption = CliOptions.CreatePauseOption();
        command.Options.Add(waitMsOption);
        command.Options.Add(pauseOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                int waitMs = parseResult.GetValue(waitMsOption) ?? 0;
                IReadOnlyList<SdlGamepadInfo> gamepads = await WaitForGamepadsAsync(
                    TimeSpan.FromMilliseconds(waitMs),
                    cancellationToken).ConfigureAwait(false);
                await PrintGamepadsAsync(gamepads).ConfigureAwait(false);
            }
            finally
            {
                await PauseIfRequestedAsync(parseResult.GetValue(pauseOption), cancellationToken).ConfigureAwait(false);
            }
        });

        return command;
    }

    internal static Command CreateInputCommand()
    {
        Command command = new("input", "Read SDL gamepad state changes.");
        Option<int?> deviceIndexOption = CliOptions.CreateDeviceIndexOption(
            "--device-index",
            "Zero-based SDL gamepad index. Default: 0.");
        Option<SdlGamepadInputMode?> modeOption = CliOptions.CreateSdlGamepadModeOption(
            "--mode",
            "SDL input mode: physical or steam. Default: steam.");
        Option<bool> physicalMotionOption = CliOptions.CreateSdlPhysicalMotionOption(
            "--physical-motion",
            "Use a physical SDL gamepad for motion and rumble while mode is steam.");
        Option<int?> motionDeviceIndexOption = CliOptions.CreateDeviceIndexOption(
            "--motion-device-index",
            "Zero-based SDL physical gamepad index for motion and rumble.");
        Option<int?> waitMsOption = CliOptions.CreateWaitMsOption(0);
        Option<bool> pauseOption = CliOptions.CreatePauseOption();
        command.Options.Add(deviceIndexOption);
        command.Options.Add(modeOption);
        command.Options.Add(physicalMotionOption);
        command.Options.Add(motionDeviceIndexOption);
        command.Options.Add(waitMsOption);
        command.Options.Add(pauseOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int waitMs = parseResult.GetValue(waitMsOption) ?? 0;
            SdlGamepadOptions options = CliOptions.CreateSdlGamepadOptions(
                parseResult,
                deviceIndexOption,
                modeOption,
                physicalMotionOption,
                motionDeviceIndexOption,
                SdlGamepadInputMode.Steam);

            await RunInputAsync(
                options,
                TimeSpan.FromMilliseconds(waitMs),
                parseResult.GetValue(pauseOption),
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static Command CreatePressCommand(IServiceProvider? services = null)
    {
        Command command = new("press", "Send a short Xbox 360 test report through VIIPER.");
        Option<int?> durationMsOption = CliOptions.CreateDurationMsOption(250);
        command.Options.Add(durationMsOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int durationMs = parseResult.GetValue(durationMsOption) ?? 250;
            _ = await ViiperConnection.ExecuteXbox360Async(
                async (output, ct) =>
                {
                    await ViiperConnection.PrintConnectionAsync(output).ConfigureAwait(false);
                    await XpadTestSender
                        .SendButtonPressAsync(output, Xbox360Buttons.A, TimeSpan.FromMilliseconds(durationMs), ct)
                        .ConfigureAwait(false);
                    await Console.Out.WriteLineAsync("xpad press: sent A press.").ConfigureAwait(false);
                    return 0;
                },
                cancellationToken,
                services).ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Helpers
    // ========================================================================

    private static async Task RunInputAsync(
        SdlGamepadOptions options,
        TimeSpan wait,
        bool pause,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        void OnCancel(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            runCancellation.Cancel();
        }

        Console.CancelKeyPress += OnCancel;

        try
        {
            using SdlGamepadSource input = await ConnectWithWaitAsync(
                options,
                wait,
                runCancellation.Token).ConfigureAwait(false);
            XpadInputPrinter printer = new();

            await Console.Out.WriteLineAsync(
                $"xpad input: mode={DisplayMode(input.Mode)} usesPhysicalMotion={FormatBool(input.UsesPhysicalMotion)} " +
                $"index={options.DeviceIndex} source={DisplaySource(input.IsSteamInput)} " +
                $"instance={input.InstanceId} steamHandle={FormatSteamHandle(input.SteamHandle)} " +
                $"vid={FormatUsbId(input.VendorId)} pid={FormatUsbId(input.ProductId)} " +
                $"gyro={FormatBool(input.HasGyro)} accel={FormatBool(input.HasAccelerometer)} " +
                $"name=\"{input.DeviceName}\". Ctrl+C to stop.")
                .ConfigureAwait(false);
            if (input.MotionDeviceName is not null)
            {
                await Console.Out.WriteLineAsync(
                    $"xpad motion: instance={input.MotionInstanceId} " +
                    $"vid={FormatUsbId(input.MotionVendorId.GetValueOrDefault())} " +
                    $"pid={FormatUsbId(input.MotionProductId.GetValueOrDefault())} " +
                    $"name=\"{input.MotionDeviceName}\"")
                    .ConfigureAwait(false);
            }

            input.Run(printer.HandleInput, runCancellation.Token);
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException exception) when (!runCancellation.IsCancellationRequested)
        {
            await Console.Error.WriteLineAsync($"xpad input: {exception.Message}").ConfigureAwait(false);
            await Console.Error.WriteLineAsync(
                "xpad input: run `test xpad probe`, then pass `--mode physical` or launch this command from Steam and use the Steam-routed index.")
                .ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
            await PauseIfRequestedAsync(pause, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PrintGamepadsAsync(IReadOnlyList<SdlGamepadInfo> gamepads)
    {
        if (gamepads.Count == 0)
        {
            await Console.Out.WriteLineAsync("no SDL gamepads found").ConfigureAwait(false);
            return;
        }

        await Console.Out.WriteLineAsync("index  source    steamHandle         vid   pid   gyro  accel  name").ConfigureAwait(false);
        foreach (SdlGamepadInfo gamepad in gamepads)
        {
            await Console.Out.WriteLineAsync(
                $"{gamepad.Index,5}  {DisplaySource(gamepad),-8}  {FormatSteamHandle(gamepad.SteamHandle),18}  " +
                $"{FormatUsbId(gamepad.VendorId),4}  {FormatUsbId(gamepad.ProductId),4}  " +
                $"{FormatBool(gamepad.HasGyro),5}  {FormatBool(gamepad.HasAccelerometer),5}  {gamepad.Name}")
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(gamepad.Path))
            {
                await Console.Out.WriteLineAsync($"       path={gamepad.Path}").ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyList<SdlGamepadInfo>> WaitForGamepadsAsync(
        TimeSpan wait,
        CancellationToken cancellationToken)
    {
        DateTimeOffset stopAt = DateTimeOffset.UtcNow + wait;
        IReadOnlyList<SdlGamepadInfo> gamepads;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            gamepads = SdlGamepadSource.GetGamepads();
            if (gamepads.Count > 0 || wait == TimeSpan.Zero)
            {
                return gamepads;
            }

            await Task.Delay(ProbeRetryInterval, cancellationToken).ConfigureAwait(false);
        }
        while (DateTimeOffset.UtcNow < stopAt);

        return gamepads;
    }

    private static async Task<SdlGamepadSource> ConnectWithWaitAsync(
        SdlGamepadOptions options,
        TimeSpan wait,
        CancellationToken cancellationToken)
    {
        DateTimeOffset stopAt = DateTimeOffset.UtcNow + wait;
        InvalidOperationException? lastException;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await SdlGamepadSource.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                lastException = exception;
                if (wait == TimeSpan.Zero)
                {
                    throw;
                }
            }

            await Task.Delay(ProbeRetryInterval, cancellationToken).ConfigureAwait(false);
        }
        while (DateTimeOffset.UtcNow < stopAt);

        throw lastException;
    }

    private static async Task PauseIfRequestedAsync(bool pause, CancellationToken cancellationToken)
    {
        if (!pause)
        {
            return;
        }

        await Console.Out.WriteLineAsync("press Enter to exit").ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested && Console.ReadKey(intercept: true).Key != ConsoleKey.Enter)
        {
        }
    }

    private static string DisplaySource(SdlGamepadInfo gamepad)
    {
        return DisplaySource(gamepad.IsSteamInput);
    }

    private static string DisplaySource(bool isSteamInput)
    {
        return isSteamInput ? "steam" : "physical";
    }

    private static string DisplayMode(SdlGamepadInputMode mode)
    {
        return mode switch
        {
            SdlGamepadInputMode.Physical => "physical",
            SdlGamepadInputMode.Steam => "steam",
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    private static string FormatSteamHandle(ulong steamHandle)
    {
        return steamHandle == 0
            ? "0"
            : $"0x{steamHandle:x16}";
    }

    private static string FormatUsbId(ushort value)
    {
        return value == 0
            ? "----"
            : $"{value:x4}";
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string DisplayVector(bool hasValue, float x, float y, float z)
    {
        return hasValue
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{x:0.###},{y:0.###},{z:0.###}")
            : "none";
    }

    private sealed class XpadInputPrinter
    {
        private static readonly long MinPrintIntervalTicks = Stopwatch.Frequency / 10;
        private long lastPrintTimestamp;

        public void HandleInput(in GamepadInput input)
        {
            long timestamp = Stopwatch.GetTimestamp();
            if (lastPrintTimestamp != 0 && timestamp - lastPrintTimestamp < MinPrintIntervalTicks)
            {
                return;
            }

            lastPrintTimestamp = timestamp;

            GamepadState state = input.State;
            Console.WriteLine(
                $"device=\"{input.DeviceName}\" " +
                $"buttons={DisplayButtons(state.Buttons)} " +
                $"lx={state.LeftX} ly={state.LeftY} rx={state.RightX} ry={state.RightY} " +
                $"lt={state.LeftTrigger} rt={state.RightTrigger} " +
                DisplayMotion(state.Motion));
        }
    }
}
