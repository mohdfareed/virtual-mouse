using System;
using System.Threading;

namespace Inputs;

/// <summary>Reads mouse reports from an input source.</summary>
public interface IMouseInputSource : IAsyncDisposable
{
    /// <summary>Gets whether the input source is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Runs the input loop until cancelled.</summary>
    /// <param name="handler">Called for each mouse report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    void Run(MouseInputHandler handler, CancellationToken cancellationToken = default);
}

/// <summary>Handles one mouse input report.</summary>
/// <param name="input">Mouse input.</param>
public delegate void MouseInputHandler(in MouseInput input);

/// <summary>Mouse input from one source.</summary>
/// <param name="Report">Mouse report.</param>
/// <param name="DeviceName">Source device name, when known.</param>
public readonly record struct MouseInput(MouseReport Report, string DeviceName);

/// <summary>Mouse button flags.</summary>
[Flags]
public enum MouseButtons
{
    /// <summary>No buttons.</summary>
    None = 0,
    /// <summary>Left button.</summary>
    Left = 1 << 0,
    /// <summary>Right button.</summary>
    Right = 1 << 1,
    /// <summary>Middle button.</summary>
    Middle = 1 << 2,
    /// <summary>Back button.</summary>
    Back = 1 << 3,
    /// <summary>Forward button.</summary>
    Forward = 1 << 4,
}

/// <summary>Relative mouse input.</summary>
/// <param name="Buttons">Current button state.</param>
/// <param name="DeltaX">Horizontal delta.</param>
/// <param name="DeltaY">Vertical delta.</param>
/// <param name="WheelDelta">Wheel delta.</param>
public readonly record struct MouseReport(
    MouseButtons Buttons,
    int DeltaX,
    int DeltaY,
    int WheelDelta)
{
    /// <summary>Empty input.</summary>
    public static MouseReport Empty => default;

    /// <summary>Gets whether the report has no input.</summary>
    public bool IsEmpty =>
        Buttons == MouseButtons.None &&
        DeltaX == 0 &&
        DeltaY == 0 &&
        WheelDelta == 0;
}
