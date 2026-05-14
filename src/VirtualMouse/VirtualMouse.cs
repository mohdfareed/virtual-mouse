using System;
using System.Threading;
using PhysicalMouse;

namespace VirtualMouse;

/// <summary>Reads mouse reports from an input source.</summary>
public interface IVirtualMouse : IAsyncDisposable
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
