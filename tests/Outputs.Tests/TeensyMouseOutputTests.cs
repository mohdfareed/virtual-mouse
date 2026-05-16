using System;
using System.Threading.Tasks;
using Outputs.Teensy;

namespace Outputs.Tests;

/// <summary>Tests for <see cref="TeensyMouseOutput" />.</summary>
[TestClass]
public sealed class TeensyMouseOutputTests
{
    /// <summary>Checks disconnected state.</summary>
    [TestMethod]
    public async Task IsConnectedIsFalse()
    {
        TeensyMouseOutput transport = new();

        try
        {
            Assert.IsFalse(transport.IsConnected);
        }
        finally
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Checks the current placeholder behavior.</summary>
    [TestMethod]
    public async Task SendAsyncThrowsNotImplementedException()
    {
        TeensyMouseOutput transport = new();

        try
        {
            try
            {
                await transport.SendAsync(MouseReport.Empty).ConfigureAwait(false);
                Assert.Fail("Expected NotImplementedException.");
            }
            catch (NotImplementedException)
            {
            }
        }
        finally
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }
}
