using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Inputs;
using Outputs;

namespace Hosting;

/// <summary>Filters one gamepad input report.</summary>
/// <param name="input">Gamepad input.</param>
/// <returns><see langword="true" /> to forward the report.</returns>
public delegate bool GamepadInputFilter(in GamepadInput input);

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
        input.RunTo(output, filter, shouldForwardMotion: null, cancellationToken);
    }

    /// <summary>Forwards filtered gamepad state to an Xbox 360 output.</summary>
    public static void RunTo(
        this IGamepadInputSource input,
        IXbox360Output output,
        GamepadInputFilter? filter,
        Func<bool>? shouldForwardMotion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        using IDisposable? rumbleSubscription = ListenRumble(input, output);
        bool hasPreviousReport = false;
        Xbox360Report previousReport = default;
        try
        {
            input.Run(HandleInput, cancellationToken);
        }
        finally
        {
            StopRumble(input);
        }

        void HandleInput(in GamepadInput source)
        {
            if (filter is not null && !filter(in source))
            {
                return;
            }

            GamepadState state = FilterMotion(source.State, shouldForwardMotion?.Invoke() ?? true);
            Xbox360Report report = ToXbox360Report(state);
            if (hasPreviousReport && report == previousReport)
            {
                return;
            }

            SendSynchronously(output, report, cancellationToken);
            previousReport = report;
            hasPreviousReport = true;
        }
    }

    internal static GamepadState FilterMotion(GamepadState state, bool motionEnabled)
    {
        return motionEnabled
            ? state
            : state with
            {
                Motion = default,
            };
    }

    internal static Xbox360Report ToXbox360Report(GamepadState state)
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

    internal static GamepadRumble ToGamepadRumble(Xbox360Rumble rumble)
    {
        return new GamepadRumble(
            ScaleRumble(rumble.LeftMotor),
            ScaleRumble(rumble.RightMotor));
    }

    private static IDisposable? ListenRumble(IGamepadInputSource input, IXbox360Output output)
    {
        return input is not IGamepadRumbleSink rumbleSink ||
            output is not IXbox360FeedbackSource feedbackSource
            ? null
            : feedbackSource.ListenRumble(rumble =>
        {
            _ = rumbleSink.TryRumble(ToGamepadRumble(rumble));
            return ValueTask.CompletedTask;
        });
    }

    private static void StopRumble(IGamepadInputSource input)
    {
        if (input is IGamepadRumbleSink rumbleSink)
        {
            _ = rumbleSink.TryRumble(GamepadRumble.Empty);
        }
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

    private static ushort ScaleRumble(byte value)
    {
        return (ushort)(value * 257);
    }

    private static void SendSynchronously(
        IXbox360Output output,
        Xbox360Report report,
        CancellationToken cancellationToken)
    {
        try
        {
            ValueTask sendTask = output.SendAsync(report, cancellationToken);
            if (sendTask.IsCompleted)
            {
                sendTask.GetAwaiter().GetResult();
                return;
            }

            sendTask.AsTask().GetAwaiter().GetResult();
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested || !output.IsConnected)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested || !output.IsConnected)
        {
        }
        catch (InvalidOperationException) when (cancellationToken.IsCancellationRequested || !output.IsConnected)
        {
        }
    }
}
