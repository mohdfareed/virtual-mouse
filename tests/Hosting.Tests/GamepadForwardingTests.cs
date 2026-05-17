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

    /// <summary>Checks motion-only updates do not resend identical Xbox reports.</summary>
    [TestMethod]
    public void RunToXbox360OutputSkipsDuplicateReports()
    {
        GamepadState first = new(GamepadButtons.South, 0, 0, 0, 0, 0, 0, default);
        GamepadState second = first with
        {
            Motion = new GamepadMotion(true, 1, 2, 3, false, 0, 0, 0),
        };
        using TestGamepadInputSource input = new([first, second]);
        using TestXbox360Output output = new();

        input.RunTo(output);

        Assert.HasCount(1, output.Reports);
        Assert.AreEqual(Xbox360Buttons.A, output.Reports[0].Buttons);
    }

    /// <summary>Checks Xbox rumble feedback maps to gamepad rumble.</summary>
    [TestMethod]
    public void RunToXbox360OutputForwardsRumbleFeedback()
    {
        using TestRumbleGamepadInputSource input = new();
        using TestXbox360Output output = new();
        input.OnRun = () => output.EmitRumble(new Xbox360Rumble(1, 2));

        input.RunTo(output);

        Assert.HasCount(2, input.Rumbles);
        Assert.AreEqual((ushort)257, input.Rumbles[0].LowFrequency);
        Assert.AreEqual((ushort)514, input.Rumbles[0].HighFrequency);
        Assert.AreEqual(GamepadRumble.Empty, input.Rumbles[1]);
    }

    /// <summary>Checks motion filtering leaves buttons and axes untouched.</summary>
    [TestMethod]
    public void FilterMotionClearsOnlyMotion()
    {
        GamepadMotion motion = new(true, 1, 2, 3, true, 4, 5, 6);
        GamepadState state = new(GamepadButtons.South, 1, 2, 3, 4, 5, 6, motion);

        GamepadState filtered = GamepadForwardingExtensions.FilterMotion(state, motionEnabled: false);

        Assert.AreEqual(state.Buttons, filtered.Buttons);
        Assert.AreEqual(state.LeftX, filtered.LeftX);
        Assert.AreEqual(state.LeftY, filtered.LeftY);
        Assert.AreEqual(state.RightX, filtered.RightX);
        Assert.AreEqual(state.RightY, filtered.RightY);
        Assert.AreEqual(state.LeftTrigger, filtered.LeftTrigger);
        Assert.AreEqual(state.RightTrigger, filtered.RightTrigger);
        Assert.AreEqual(default, filtered.Motion);
    }

    private class TestGamepadInputSource(GamepadState[] states) : IGamepadInputSource, IDisposable
    {
        private readonly GamepadState[] _states = states;

        public TestGamepadInputSource(GamepadState state)
            : this([state])
        {
        }

        public bool IsConnected => true;

        public Action? OnRun { get; set; }

        public void Run(GamepadInputHandler handler, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnRun?.Invoke();
            foreach (GamepadState state in _states)
            {
                GamepadInput input = new(state, string.Empty);
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

    private sealed class TestRumbleGamepadInputSource : TestGamepadInputSource, IGamepadRumbleSink
    {
        public TestRumbleGamepadInputSource()
            : base(GamepadState.Empty)
        {
        }

        public List<GamepadRumble> Rumbles { get; } = [];

        public bool TryRumble(GamepadRumble rumble)
        {
            Rumbles.Add(rumble);
            return true;
        }
    }

    private sealed class TestXbox360Output : IXbox360Output, IXbox360FeedbackSource, IDisposable
    {
        private Xbox360RumbleHandler? _rumbleHandler;

        public bool IsConnected => true;

        public List<Xbox360Report> Reports { get; } = [];

        public ValueTask SendAsync(Xbox360Report report, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reports.Add(report);
            return ValueTask.CompletedTask;
        }

        public IDisposable ListenRumble(Xbox360RumbleHandler handler)
        {
            _rumbleHandler = handler;
            return new RumbleSubscription(() => _rumbleHandler = null);
        }

        public void EmitRumble(Xbox360Rumble rumble)
        {
            if (_rumbleHandler is not null)
            {
                _rumbleHandler(rumble).AsTask().GetAwaiter().GetResult();
            }
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
        }

        private sealed class RumbleSubscription(Action dispose) : IDisposable
        {
            public void Dispose()
            {
                dispose();
            }
        }
    }
}
