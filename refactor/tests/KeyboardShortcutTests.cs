using System;
using VirtualMouse.Shortcuts;

namespace VirtualMouse.Tests;

[TestClass]
public sealed class KeyboardShortcutTests
{
    [TestMethod]
    public void ParsesModifierFunctionKeyCombination()
    {
        KeyboardShortcutCombination shortcut = KeyboardShortcutParser.Parse("Ctrl+Alt+F13");

        Assert.AreEqual(
            KeyboardShortcutModifiers.Control | KeyboardShortcutModifiers.Alt,
            shortcut.Modifiers);
        Assert.AreEqual((ushort)0x7c, shortcut.VirtualKey);
    }

    [TestMethod]
    public void RejectsCombinationWithoutKey()
    {
        FormatException exception = Assert.ThrowsExactly<FormatException>(
            static () => KeyboardShortcutParser.Parse("Ctrl+Alt"));

        StringAssert.Contains(exception.Message, "does not contain a key", StringComparison.Ordinal);
    }
}
