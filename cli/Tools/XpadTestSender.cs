using System;
using System.Threading;
using System.Threading.Tasks;
using Outputs;

namespace Cli.Tools;

internal static class XpadTestSender
{
    internal static async Task SendButtonPressAsync(
        IXbox360Output output,
        Xbox360Buttons buttons,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);

        Xbox360Report pressed = new(buttons, 0, 0, 0, 0, 0, 0);
        await output.SendAsync(pressed, cancellationToken).ConfigureAwait(false);
        await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        await output.SendAsync(Xbox360Report.Empty, cancellationToken).ConfigureAwait(false);
    }
}
