using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hosting.Tests;

/// <summary>Tests for mouse forwarding routes.</summary>
[TestClass]
public sealed class MouseForwardingTests
{
    /// <summary>Checks direct forwarding.</summary>
    [TestMethod]
    public void RunToForwardsNonEmptyReports()
    {
        MouseReport report = new(MouseButtons.Left, 1, -2, 0);
        using TestMouseInputSource input = new(new MouseInput(report, "device"));
        using TestMouseOutput output = new();

        input.RunTo(output);

        Assert.HasCount(1, output.Reports);
        Assert.AreEqual(report, output.Reports[0]);
    }

    /// <summary>Checks empty report filtering.</summary>
    [TestMethod]
    public void RunToSkipsEmptyReports()
    {
        using TestMouseInputSource input = new(new MouseInput(MouseReport.Empty, "device"));
        using TestMouseOutput output = new();

        input.RunTo(output);

        Assert.HasCount(0, output.Reports);
    }

    /// <summary>Checks input filtering.</summary>
    [TestMethod]
    public void RunToSkipsFilteredReports()
    {
        MouseReport report = new(MouseButtons.Left, 1, -2, 0);
        using TestMouseInputSource input = new(new MouseInput(report, "owned"));
        using TestMouseOutput output = new();

        input.RunTo(output, IsNotOwned);

        Assert.HasCount(0, output.Reports);
    }

    /// <summary>Checks transport-owned input filtering.</summary>
    [TestMethod]
    public void RunToAppliesOutputFilter()
    {
        MouseReport report = new(MouseButtons.Left, 1, -2, 0);
        using TestMouseInputSource input = new(new MouseInput(report, "owned"));
        using TestMouseOutput output = new(IsNotOwned);

        input.RunTo(output);

        Assert.HasCount(0, output.Reports);
    }

    private sealed class TestMouseInputSource(params MouseInput[] inputs) : IMouseInputSource, IDisposable
    {
        public bool IsConnected => true;

        public void Run(MouseInputHandler handler, CancellationToken cancellationToken = default)
        {
            foreach (MouseInput input in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                handler(in input);
            }
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private static bool IsNotOwned(in MouseInput input)
    {
        return !string.Equals(input.DeviceName, "owned", StringComparison.Ordinal);
    }

    private sealed class TestMouseOutput(MouseInputFilter? filter = null) : IMouseOutput, IDisposable
    {
        public bool IsConnected => true;

        public List<MouseReport> Reports { get; } = [];

        public bool FilterInput(string? deviceName)
        {
            MouseInput input = new(MouseReport.Empty, deviceName ?? string.Empty);
            return filter?.Invoke(in input) != false;
        }

        public ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
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
