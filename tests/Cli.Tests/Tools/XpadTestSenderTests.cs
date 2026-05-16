using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cli.Tools;

namespace Cli.Tests.Tools;

/// <summary>Tests for the CLI xpad test sender.</summary>
[TestClass]
public sealed class XpadTestSenderTests
{
    /// <summary>Checks the Xbox test press helper.</summary>
    [TestMethod]
    public async Task SendButtonPressAsyncSendsPressAndRelease()
    {
        using TestXbox360Output output = new();

        await XpadTestSender
            .SendButtonPressAsync(output, Xbox360Buttons.A, TimeSpan.Zero)
            .ConfigureAwait(false);

        Assert.HasCount(2, output.Reports);
        Assert.AreEqual(Xbox360Buttons.A, output.Reports[0].Buttons);
        Assert.AreEqual(Xbox360Report.Empty, output.Reports[1]);
    }

    private sealed class TestXbox360Output : IXbox360Output, IDisposable
    {
        public bool IsConnected => true;

        public List<Xbox360Report> Reports { get; } = [];

        public ValueTask SendAsync(Xbox360Report report, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reports.Add(report);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
