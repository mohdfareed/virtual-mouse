using Cli.Tools;

namespace Cli.Tests.Tools;

/// <summary>Tests for the CLI mouse nullifier tool.</summary>
[TestClass]
public sealed class MouseNullifierTests
{
    /// <summary>Checks movement inversion.</summary>
    [TestMethod]
    public void NullifyFlipsMovementOnly()
    {
        MouseReport report = new(MouseButtons.Left | MouseButtons.Right, 12, -34, 5);

        MouseReport inverted = MouseNullifier.Nullify(report);

        Assert.AreEqual(MouseButtons.None, inverted.Buttons);
        Assert.AreEqual(-12, inverted.DeltaX);
        Assert.AreEqual(34, inverted.DeltaY);
        Assert.AreEqual(0, inverted.WheelDelta);
    }
}
