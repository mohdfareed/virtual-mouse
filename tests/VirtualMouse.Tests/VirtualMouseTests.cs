using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhysicalMouse;

namespace VirtualMouse.Tests;

/// <summary>Tests for virtual mouse contracts.</summary>
[TestClass]
public sealed class VirtualMouseTests
{
    /// <summary>Checks input value storage.</summary>
    [TestMethod]
    public void MouseInputStoresReportAndDeviceName()
    {
        MouseReport report = new(MouseButtons.Left, 1, -2, 0);

        MouseInput input = new(report, "device");

        Assert.AreEqual(report, input.Report);
        Assert.AreEqual("device", input.DeviceName);
    }

    /// <summary>Checks direct forwarding.</summary>
    [TestMethod]
    public void RunToForwardsNonEmptyReports()
    {
        MouseReport report = new(MouseButtons.Left, 1, -2, 0);
        using TestVirtualMouse input = new(new MouseInput(report, "device"));
        using TestPhysicalMouse output = new();

        input.RunTo(output);

        Assert.HasCount(1, output.Reports);
        Assert.AreEqual(report, output.Reports[0]);
    }

    /// <summary>Checks empty report filtering.</summary>
    [TestMethod]
    public void RunToSkipsEmptyReports()
    {
        using TestVirtualMouse input = new(new MouseInput(MouseReport.Empty, "device"));
        using TestPhysicalMouse output = new();

        input.RunTo(output);

        Assert.HasCount(0, output.Reports);
    }

    /// <summary>Checks input filtering.</summary>
    [TestMethod]
    public void RunToSkipsFilteredReports()
    {
        MouseReport report = new(MouseButtons.Left, 1, -2, 0);
        using TestVirtualMouse input = new(new MouseInput(report, "owned"));
        using TestPhysicalMouse output = new();

        input.RunTo(output, IsNotOwned);

        Assert.HasCount(0, output.Reports);
    }

    /// <summary>Checks transport-owned input filtering.</summary>
    [TestMethod]
    public void RunToAppliesOutputFilter()
    {
        MouseReport report = new(MouseButtons.Left, 1, -2, 0);
        using TestVirtualMouse input = new(new MouseInput(report, "owned"));
        using TestPhysicalMouse output = new(IsNotOwned);

        input.RunTo(output);

        Assert.HasCount(0, output.Reports);
    }

    /// <summary>Checks report transforms.</summary>
    [TestMethod]
    public void RunToAppliesTransform()
    {
        MouseReport report = new(MouseButtons.Left, 1, -2, 1);
        using TestVirtualMouse input = new(new MouseInput(report, "device"));
        using TestPhysicalMouse output = new();

        input.RunTo(output, InvertMovement);

        Assert.HasCount(1, output.Reports);
        Assert.AreEqual(MouseButtons.None, output.Reports[0].Buttons);
        Assert.AreEqual(-1, output.Reports[0].DeltaX);
        Assert.AreEqual(2, output.Reports[0].DeltaY);
        Assert.AreEqual(0, output.Reports[0].WheelDelta);
    }

    private sealed class TestVirtualMouse(params MouseInput[] inputs) : IVirtualMouse, IDisposable
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

    private static MouseReport InvertMovement(MouseReport report)
    {
        return new MouseReport(MouseButtons.None, -report.DeltaX, -report.DeltaY, 0);
    }

    private sealed class TestPhysicalMouse(MouseInputFilter? filter = null) : IPhysicalMouse, IDisposable
    {
        public bool IsConnected => true;

        public List<MouseReport> Reports { get; } = [];

        public bool FilterInput(in MouseInput input)
        {
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
