using SteamInputBridge.Forwarding.Controller;

namespace SteamInputBridge.Tests;

/// <summary>Tests concrete controller report mapping.</summary>
[TestClass]
public sealed class ControllerOutputMappingTests
{
    /// <summary>Maps standard controller controls to Xbox 360 report fields.</summary>
    [TestMethod]
    public void MapsStandardStateToXbox360Report()
    {
        ControllerButtons buttons =
            ControllerButtons.South |
            ControllerButtons.East |
            ControllerButtons.West |
            ControllerButtons.North |
            ControllerButtons.Back |
            ControllerButtons.Guide |
            ControllerButtons.Start |
            ControllerButtons.LeftStick |
            ControllerButtons.RightStick |
            ControllerButtons.LeftShoulder |
            ControllerButtons.RightShoulder |
            ControllerButtons.DPadUp |
            ControllerButtons.DPadDown |
            ControllerButtons.DPadLeft |
            ControllerButtons.DPadRight;
        ControllerState state = new(
            new ControllerStandardState(buttons, 10, -20, 30, short.MinValue, 32767, 16384),
            null,
            null);

        Xbox360Report report = ControllerOutputMapping.ToXbox360Report(in state);

        Xbox360Buttons expected =
            Xbox360Buttons.A |
            Xbox360Buttons.B |
            Xbox360Buttons.X |
            Xbox360Buttons.Y |
            Xbox360Buttons.Back |
            Xbox360Buttons.Guide |
            Xbox360Buttons.Start |
            Xbox360Buttons.LeftThumb |
            Xbox360Buttons.RightThumb |
            Xbox360Buttons.LeftShoulder |
            Xbox360Buttons.RightShoulder |
            Xbox360Buttons.DPadUp |
            Xbox360Buttons.DPadDown |
            Xbox360Buttons.DPadLeft |
            Xbox360Buttons.DPadRight;
        Assert.AreEqual(expected, report.Buttons);
        Assert.AreEqual(255, report.LeftTrigger);
        Assert.AreEqual(127, report.RightTrigger);
        Assert.AreEqual(10, report.LeftX);
        Assert.AreEqual(20, report.LeftY);
        Assert.AreEqual(30, report.RightX);
        Assert.AreEqual(short.MaxValue, report.RightY);
    }

    /// <summary>Missing standard controls map to a centered Xbox report.</summary>
    [TestMethod]
    public void MissingStandardStateMapsToEmptyXbox360Report()
    {
        Xbox360Report report = ControllerOutputMapping.ToXbox360Report(ControllerState.Empty);

        Assert.IsTrue(report.IsEmpty);
    }

    /// <summary>Maps Xbox rumble bytes to canonical feedback intensities.</summary>
    [TestMethod]
    public void MapsXbox360RumbleToControllerFeedback()
    {
        ControllerFeedback feedback = ControllerOutputMapping.ToControllerFeedback(
            new Xbox360Rumble(byte.MaxValue, 128));

        Assert.AreEqual(ushort.MaxValue, feedback.Rumble?.LowFrequency);
        Assert.AreEqual((ushort)32896, feedback.Rumble?.HighFrequency);
    }
}
