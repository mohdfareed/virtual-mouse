using System;
using System.Threading;
using System.Threading.Tasks;
using Inputs;

namespace Outputs;

/// <summary>Sends mouse reports to a transport.</summary>
public interface IMouseOutput : IAsyncDisposable
{
    /// <summary>Gets whether the transport is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Returns whether the input should be forwarded to this transport.</summary>
    /// <param name="deviceName">Source device name, when known.</param>
    /// <remarks>This filter can be used to resolve input conflicts.</remarks>
    bool FilterInput(string? deviceName)
    {
        _ = deviceName;
        return true;
    }

    /// <summary>Sends one mouse report.</summary>
    /// <param name="report">Report to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default);
}
