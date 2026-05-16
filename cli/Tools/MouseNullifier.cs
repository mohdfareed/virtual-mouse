using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Inputs;
using Inputs.RawInput;
using Outputs;

namespace Cli.Tools;

internal static class MouseNullifier
{
    [SupportedOSPlatform("windows")]
    public static async Task RunRawInputToAsync(
        IMouseOutput output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);

        using RawInputMouseSource input = await RawInputMouseSource
            .ConnectAsync(cancellationToken)
            .ConfigureAwait(false);

        input.Run(HandleInput, cancellationToken);

        void HandleInput(in MouseInput source)
        {
            if (!output.FilterInput(source.DeviceName))
            {
                return;
            }

            MouseReport report = Nullify(source.Report);
            if (!report.IsEmpty)
            {
                SendSynchronously(output, report, cancellationToken);
            }
        }
    }

    internal static MouseReport Nullify(MouseReport report)
    {
        return new MouseReport(MouseButtons.None, -report.DeltaX, -report.DeltaY, 0);
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
