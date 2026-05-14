using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhysicalMouse;

/// <summary>Sends mouse reports to a transport.</summary>
public interface IPhysicalMouse : IAsyncDisposable
{
    /// <summary>Gets whether the transport is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Returns whether the input should be forwarded to this transport.</summary>
    /// <param name="input">Mouse input.</param>
    bool FilterInput(in MouseInput input)
    {
        return true;
    }

    /// <summary>Sends one mouse report.</summary>
    /// <param name="report">Report to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default);
}

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

/// <summary>Mouse input from one source.</summary>
/// <param name="Report">Mouse report.</param>
/// <param name="DeviceName">Source device name, when known.</param>
public readonly record struct MouseInput(MouseReport Report, string DeviceName);

/// <summary>Common mouse report transforms.</summary>
public static class MouseReportTransforms
{
    /// <summary>Returns opposite movement with no button or wheel input.</summary>
    public static MouseReport NullifyMovement(MouseReport report)
    {
        return new MouseReport(MouseButtons.None, -report.DeltaX, -report.DeltaY, 0);
    }
}
