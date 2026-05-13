namespace PhysicalMouse.Tests;

/// <summary>Tests for Steam CLI helpers.</summary>
[TestClass]
public sealed class CliSteamCommandsTests
{
    /// <summary>Checks movement inversion.</summary>
    [TestMethod]
    public void NullifyFlipsMovementAndWheel()
    {
        MouseReport report = new(MouseButtons.Left | MouseButtons.Right, 12, -34, 5);

        MouseReport inverted = CliSteamCommands.Nullify(report);

        Assert.AreEqual(report.Buttons, inverted.Buttons);
        Assert.AreEqual(-12, inverted.DeltaX);
        Assert.AreEqual(34, inverted.DeltaY);
        Assert.AreEqual(-5, inverted.WheelDelta);
    }
}
