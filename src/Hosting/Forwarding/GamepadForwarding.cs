using System;
using System.Threading;
using System.Threading.Tasks;
using Inputs;
using Inputs.Sdl;
using Outputs;

namespace Hosting;

/// <summary>Filters one gamepad input report.</summary>
/// <param name="input">Gamepad input.</param>
/// <returns><see langword="true" /> to forward the report.</returns>
public delegate bool GamepadInputFilter(in GamepadInput input);

/// <summary>Connected gamepad forwarding session.</summary>
/// <param name="DeviceName">Input device name.</param>
public readonly record struct GamepadForwardingSession(string DeviceName);

/// <summary>Handles a connected gamepad forwarding session.</summary>
/// <param name="session">Connected session.</param>
/// <param name="cancellationToken">Cancellation token.</param>
public delegate ValueTask GamepadForwardingConnectedHandler(
    GamepadForwardingSession session,
    CancellationToken cancellationToken);

/// <summary>Gamepad forwarding helpers.</summary>
public static class GamepadForwarding
{
    /// <summary>Forwards SDL gamepad state to an Xbox 360 output.</summary>
    public static async Task RunSdlToXbox360Async(
        IXbox360Output output,
        SdlGamepadOptions options,
        GamepadForwardingConnectedHandler? connected = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);

        using SdlGamepadSource input = await SdlGamepadSource
            .ConnectAsync(options, cancellationToken)
            .ConfigureAwait(false);

        if (connected is not null)
        {
            await connected(
                new GamepadForwardingSession(input.DeviceName),
                cancellationToken).ConfigureAwait(false);
        }

        input.RunTo(output, cancellationToken);
    }
}

/// <summary>Forwards canonical gamepad input to output devices.</summary>
public static class GamepadForwardingExtensions
{
    /// <summary>Forwards gamepad state to an Xbox 360 output.</summary>
    public static void RunTo(
        this IGamepadInputSource input,
        IXbox360Output output,
        CancellationToken cancellationToken = default)
    {
        input.RunTo(output, filter: null, cancellationToken);
    }

    /// <summary>Forwards filtered gamepad state to an Xbox 360 output.</summary>
    public static void RunTo(
        this IGamepadInputSource input,
        IXbox360Output output,
        GamepadInputFilter? filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        input.Run(HandleInput, cancellationToken);

        void HandleInput(in GamepadInput source)
        {
            if (filter is not null && !filter(in source))
            {
                return;
            }

            SendSynchronously(output, ToXbox360Report(source.State), cancellationToken);
        }
    }

    private static Xbox360Report ToXbox360Report(GamepadState state)
    {
        Xbox360Buttons buttons = Xbox360Buttons.None;
        buttons = Apply(buttons, state.Buttons, GamepadButtons.South, Xbox360Buttons.A);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.East, Xbox360Buttons.B);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.West, Xbox360Buttons.X);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.North, Xbox360Buttons.Y);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.Back, Xbox360Buttons.Back);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.Guide, Xbox360Buttons.Guide);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.Start, Xbox360Buttons.Start);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.LeftStick, Xbox360Buttons.LeftThumb);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.RightStick, Xbox360Buttons.RightThumb);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.LeftShoulder, Xbox360Buttons.LeftShoulder);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.RightShoulder, Xbox360Buttons.RightShoulder);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.DPadUp, Xbox360Buttons.DPadUp);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.DPadDown, Xbox360Buttons.DPadDown);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.DPadLeft, Xbox360Buttons.DPadLeft);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.DPadRight, Xbox360Buttons.DPadRight);

        return new Xbox360Report(
            buttons,
            ToByteTrigger(state.LeftTrigger),
            ToByteTrigger(state.RightTrigger),
            state.LeftX,
            InvertAxis(state.LeftY),
            state.RightX,
            InvertAxis(state.RightY));
    }

    private static Xbox360Buttons Apply(
        Xbox360Buttons output,
        GamepadButtons input,
        GamepadButtons inputButton,
        Xbox360Buttons outputButton)
    {
        return (input & inputButton) != 0 ? output | outputButton : output;
    }

    private static byte ToByteTrigger(ushort value)
    {
        return (byte)Math.Clamp(value * 255 / 32767, byte.MinValue, byte.MaxValue);
    }

    private static short InvertAxis(short value)
    {
        return value == short.MinValue ? short.MaxValue : (short)-value;
    }

    private static void SendSynchronously(
        IXbox360Output output,
        Xbox360Report report,
        CancellationToken cancellationToken)
    {
        ValueTask sendTask = output.SendAsync(report, cancellationToken);
        if (sendTask.IsCompleted)
        {
            sendTask.GetAwaiter().GetResult();
            return;
        }

        sendTask.AsTask().GetAwaiter().GetResult();
    }
}
