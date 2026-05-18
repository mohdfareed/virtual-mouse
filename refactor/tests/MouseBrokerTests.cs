using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VirtualMouse.Forwarding;

namespace VirtualMouse.Tests;

/// <summary>Tests mouse forwarding broker behavior.</summary>
[TestClass]
public sealed class MouseBrokerTests
{
    /// <summary>Mouse output connects only while the active client wants it.</summary>
    [TestMethod]
    public void OutputConnectsAndDisconnectsWithActiveClient()
    {
        Guid clientId = Guid.NewGuid();
        FakeMouseOutputFactory factory = new();
        using MouseBroker broker = new(factory);

        broker.RegisterClient(clientId, MouseOutput.Viiper);
        Assert.IsEmpty(factory.Outputs);

        broker.SetActiveClient(clientId);
        Assert.HasCount(1, factory.Outputs);
        Assert.IsFalse(factory.Outputs[0].Disposed);

        broker.SetMouseOutputEnabled(false);
        Assert.IsTrue(factory.Outputs[0].Disposed);
    }

    /// <summary>Mouse reports are sent only when output is active and non-empty.</summary>
    [TestMethod]
    public void SendsOnlyActiveNonEmptyReports()
    {
        Guid clientId = Guid.NewGuid();
        FakeMouseOutputFactory factory = new();
        using MouseBroker broker = new(factory);

        broker.RegisterClient(clientId, MouseOutput.Viiper);
        broker.Send(new MouseInput(new MouseReport(MouseButtons.Left, 1, 2, 0), "mouse"));
        broker.SetActiveClient(clientId);
        broker.Send(new MouseInput(MouseReport.Empty, "mouse"));
        broker.Send(new MouseInput(new MouseReport(MouseButtons.Left, 1, 2, 0), "mouse"));

        Assert.HasCount(1, factory.Outputs[0].Reports);
        Assert.AreEqual(MouseButtons.Left, factory.Outputs[0].Reports[0].Buttons);
    }

    private sealed class FakeMouseOutputFactory : IMouseOutputFactory
    {
        public List<FakeMouseOutput> Outputs { get; } = [];

        public IMouseOutput Connect(MouseOutput output)
        {
            FakeMouseOutput connected = new(output);
            Outputs.Add(connected);
            return connected;
        }
    }

    private sealed class FakeMouseOutput(MouseOutput output) : IMouseOutput
    {
        public MouseOutput Output { get; } = output;

        public bool IsConnected => !Disposed;

        public bool Disposed { get; private set; }

        public List<MouseReport> Reports { get; } = [];

        public ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
        {
            Reports.Add(report);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
