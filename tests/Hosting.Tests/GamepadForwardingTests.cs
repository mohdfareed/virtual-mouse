using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hosting.Tests;

/// <summary>Tests for gamepad forwarding routes.</summary>
[TestClass]
public sealed class GamepadForwardingTests
{
    /// <summary>Checks standard face and shoulder buttons map to Xbox buttons.</summary>
    [TestMethod]
    public void RunToXbox360OutputMapsButtons()
    {
        GamepadState state = new(
            GamepadButtons.South | GamepadButtons.East | GamepadButtons.LeftShoulder | GamepadButtons.DPadUp,
            0,
            0,
            0,
            0,
            0,
            0,
            default);
        using TestGamepadInputSource input = new(state);
        using TestXbox360Output output = new();

        input.RunTo(output);

        Assert.HasCount(1, output.Reports);
        Assert.AreEqual(
            Xbox360Buttons.A | Xbox360Buttons.B | Xbox360Buttons.LeftShoulder | Xbox360Buttons.DPadUp,
            output.Reports[0].Buttons);
    }

    /// <summary>Checks trigger conversion.</summary>
    [TestMethod]
    public void RunToXbox360OutputScalesTriggersToBytes()
    {
        GamepadState state = new(GamepadButtons.None, 0, 0, 0, 0, 32767, 0, default);
        using TestGamepadInputSource input = new(state);
        using TestXbox360Output output = new();

        input.RunTo(output);

        Assert.HasCount(1, output.Reports);
        Assert.AreEqual(byte.MaxValue, output.Reports[0].LeftTrigger);
        Assert.AreEqual(byte.MinValue, output.Reports[0].RightTrigger);
    }

    /// <summary>Checks Y axis inversion for Xbox output.</summary>
    [TestMethod]
    public void RunToXbox360OutputInvertsYAxis()
    {
        GamepadState state = new(GamepadButtons.None, 1, -2, 3, 4, 0, 0, default);
        using TestGamepadInputSource input = new(state);
        using TestXbox360Output output = new();

        input.RunTo(output);

        Assert.HasCount(1, output.Reports);
        Assert.AreEqual((short)1, output.Reports[0].LeftX);
        Assert.AreEqual((short)2, output.Reports[0].LeftY);
        Assert.AreEqual((short)3, output.Reports[0].RightX);
        Assert.AreEqual((short)-4, output.Reports[0].RightY);
    }

    private sealed class TestGamepadInputSource(GamepadState state) : IGamepadInputSource, IDisposable
    {
        public bool IsConnected => true;

        public void Run(GamepadInputHandler handler, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GamepadInput input = new(state, string.Empty);
            handler(in input);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
        }
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
