namespace PhysicalMouse.Tests;

/// <summary>Tests for Steam CLI helpers.</summary>
[TestClass]
public sealed class CliSteamCommandsTests
{
    /// <summary>Checks movement inversion.</summary>
    [TestMethod]
    public void NullifyFlipsMovementOnly()
    {
        MouseReport report = new(MouseButtons.Left | MouseButtons.Right, 12, -34, 5);

        MouseReport inverted = CliSteamCommands.Nullify(report);

        Assert.AreEqual(MouseButtons.None, inverted.Buttons);
        Assert.AreEqual(-12, inverted.DeltaX);
        Assert.AreEqual(34, inverted.DeltaY);
        Assert.AreEqual(0, inverted.WheelDelta);
    }

    /// <summary>Checks forward mode.</summary>
    [TestMethod]
    public void ApplyModePreservesReportInForwardMode()
    {
        MouseReport report = new(MouseButtons.Left, 12, -34, 5);

        MouseReport forwarded = CliSteamCommands.ApplyMode(report, SteamMouseMode.Forward);

        Assert.AreEqual(report, forwarded);
    }

}
