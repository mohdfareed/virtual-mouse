using System;
using System.Threading;
using System.Threading.Tasks;

namespace Outputs;

/// <summary>Sends Xbox 360 controller reports to an output device.</summary>
public interface IXbox360Output : IAsyncDisposable
{
    /// <summary>Gets whether the output device is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Sends one Xbox 360 controller state report.</summary>
    /// <param name="report">Controller report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SendAsync(Xbox360Report report, CancellationToken cancellationToken = default);
}

/// <summary>Receives Xbox 360 output feedback.</summary>
public interface IXbox360FeedbackSource
{
    /// <summary>Registers for rumble feedback.</summary>
    /// <param name="handler">Called for each rumble update.</param>
    /// <returns>Subscription that stops feedback delivery when disposed.</returns>
    IDisposable ListenRumble(Xbox360RumbleHandler handler);
}

/// <summary>Handles one Xbox 360 rumble feedback update.</summary>
/// <param name="rumble">Rumble feedback.</param>
public delegate ValueTask Xbox360RumbleHandler(Xbox360Rumble rumble);

/// <summary>Xbox 360 button flags.</summary>
[Flags]
public enum Xbox360Buttons
{
    /// <summary>No buttons.</summary>
    None = 0,
    /// <summary>D-pad up.</summary>
    DPadUp = 1 << 0,
    /// <summary>D-pad down.</summary>
    DPadDown = 1 << 1,
    /// <summary>D-pad left.</summary>
    DPadLeft = 1 << 2,
    /// <summary>D-pad right.</summary>
    DPadRight = 1 << 3,
    /// <summary>Start button.</summary>
    Start = 1 << 4,
    /// <summary>Back button.</summary>
    Back = 1 << 5,
    /// <summary>Left thumbstick button.</summary>
    LeftThumb = 1 << 6,
    /// <summary>Right thumbstick button.</summary>
    RightThumb = 1 << 7,
    /// <summary>Left shoulder button.</summary>
    LeftShoulder = 1 << 8,
    /// <summary>Right shoulder button.</summary>
    RightShoulder = 1 << 9,
    /// <summary>Guide button.</summary>
    Guide = 1 << 10,
    /// <summary>A button.</summary>
    A = 1 << 12,
    /// <summary>B button.</summary>
    B = 1 << 13,
    /// <summary>X button.</summary>
    X = 1 << 14,
    /// <summary>Y button.</summary>
    Y = 1 << 15,
}

/// <summary>Xbox 360 controller state.</summary>
/// <param name="Buttons">Pressed buttons.</param>
/// <param name="LeftTrigger">Left trigger value.</param>
/// <param name="RightTrigger">Right trigger value.</param>
/// <param name="LeftX">Left stick horizontal axis.</param>
/// <param name="LeftY">Left stick vertical axis.</param>
/// <param name="RightX">Right stick horizontal axis.</param>
/// <param name="RightY">Right stick vertical axis.</param>
public readonly record struct Xbox360Report(
    Xbox360Buttons Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short LeftX,
    short LeftY,
    short RightX,
    short RightY)
{
    /// <summary>Centered controller state.</summary>
    public static Xbox360Report Empty => default;

    /// <summary>Gets whether the report is centered with no pressed inputs.</summary>
    public bool IsEmpty =>
        Buttons == Xbox360Buttons.None &&
        LeftTrigger == 0 &&
        RightTrigger == 0 &&
        LeftX == 0 &&
        LeftY == 0 &&
        RightX == 0 &&
        RightY == 0;
}

/// <summary>Xbox 360 rumble feedback.</summary>
/// <param name="LeftMotor">Left motor intensity.</param>
/// <param name="RightMotor">Right motor intensity.</param>
public readonly record struct Xbox360Rumble(byte LeftMotor, byte RightMotor);
