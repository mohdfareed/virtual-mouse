using System;
using System.Threading;
using System.Threading.Tasks;
using PhysicalMouse;

namespace VirtualMouse;

/// <summary>Filters one mouse input report.</summary>
/// <param name="input">Mouse input.</param>
/// <returns><see langword="true" /> to forward the report.</returns>
public delegate bool MouseInputFilter(in MouseInput input);

/// <summary>Transforms one mouse report.</summary>
/// <param name="report">Mouse report.</param>
/// <returns>Transformed report.</returns>
public delegate MouseReport MouseReportTransform(MouseReport report);

/// <summary>Forwards virtual mouse input to physical mouse output.</summary>
public static class VirtualMouseForwardingExtensions
{
    /// <summary>Forwards input reports to a physical mouse.</summary>
    public static void RunTo(
        this IVirtualMouse input,
        IPhysicalMouse output,
        CancellationToken cancellationToken = default)
    {
        input.RunTo(output, filter: null, transform: null, cancellationToken);
    }

    /// <summary>Forwards transformed input reports to a physical mouse.</summary>
    public static void RunTo(
        this IVirtualMouse input,
        IPhysicalMouse output,
        MouseReportTransform transform,
        CancellationToken cancellationToken = default)
    {
        input.RunTo(output, filter: null, transform, cancellationToken);
    }

    /// <summary>Forwards filtered input reports to a physical mouse.</summary>
    public static void RunTo(
        this IVirtualMouse input,
        IPhysicalMouse output,
        MouseInputFilter filter,
        CancellationToken cancellationToken = default)
    {
        input.RunTo(output, filter, transform: null, cancellationToken);
    }

    /// <summary>Forwards filtered and transformed input reports to a physical mouse.</summary>
    public static void RunTo(
        this IVirtualMouse input,
        IPhysicalMouse output,
        MouseInputFilter? filter,
        MouseReportTransform? transform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        input.Run(HandleInput, cancellationToken);

        void HandleInput(in MouseInput source)
        {
            if (!output.FilterInput(in source))
            {
                return;
            }

            if (filter is not null && !filter(in source))
            {
                return;
            }

            MouseReport report = transform is null
                ? source.Report
                : transform(source.Report);

            if (!report.IsEmpty)
            {
                SendSynchronously(output, report, cancellationToken);
            }
        }
    }

    private static void SendSynchronously(
        IPhysicalMouse output,
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
