namespace PhysicalMouse.Tests;

/// <summary>Tests for input CLI helpers.</summary>
[TestClass]
public sealed class CliInputCommandsTests
{
    /// <summary>Checks empty button formatting.</summary>
    [TestMethod]
    public void DisplayButtonsReturnsNoneForEmptyButtons()
    {
        Assert.AreEqual("none", CliInputCommands.DisplayButtons(MouseButtons.None));
    }

    /// <summary>Checks non-empty button formatting.</summary>
    [TestMethod]
    public void DisplayButtonsReturnsNamedButtons()
    {
        Assert.AreEqual(
            "Left, Right",
            CliInputCommands.DisplayButtons(MouseButtons.Left | MouseButtons.Right));
    }
}
