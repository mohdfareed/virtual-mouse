using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Inputs;
using Inputs.RawInput;
using Outputs;

namespace Hosting;

/// <summary>Filters one mouse input report.</summary>
/// <param name="input">Mouse input.</param>
/// <returns><see langword="true" /> to forward the report.</returns>
public delegate bool MouseInputFilter(in MouseInput input);

/// <summary>Mouse forwarding helpers.</summary>
public static class MouseForwarding
{
    /// <summary>Forwards Raw Input mouse reports to a mouse output.</summary>
    [SupportedOSPlatform("windows")]
    public static async Task RunRawInputToAsync(
        IMouseOutput output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);

        using RawInputMouseSource input = await RawInputMouseSource
            .ConnectAsync(cancellationToken)
            .ConfigureAwait(false);

        input.RunTo(output, cancellationToken);
    }
}

/// <summary>Forwards mouse input to mouse output.</summary>
public static class MouseForwardingExtensions
{
    /// <summary>Forwards input reports to a mouse output.</summary>
    public static void RunTo(
        this IMouseInputSource input,
        IMouseOutput output,
        CancellationToken cancellationToken = default)
    {
        input.RunTo(output, filter: null, cancellationToken);
    }

    /// <summary>Forwards filtered input reports to a mouse output.</summary>
    public static void RunTo(
        this IMouseInputSource input,
        IMouseOutput output,
        MouseInputFilter? filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        input.Run(HandleInput, cancellationToken);

        void HandleInput(in MouseInput source)
        {
            if (!output.FilterInput(source.DeviceName))
            {
                return;
            }

            if (filter is not null && !filter(in source))
            {
                return;
            }

            if (!source.Report.IsEmpty)
            {
                SendSynchronously(output, source.Report, cancellationToken);
            }
        }
    }

    private static void SendSynchronously(
        IMouseOutput output,
        MouseReport report,
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
