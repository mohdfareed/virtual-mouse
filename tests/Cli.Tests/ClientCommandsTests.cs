using System.CommandLine;

namespace Cli.Tests;

#pragma warning disable CA1416

/// <summary>Tests for client CLI helpers.</summary>
[TestClass]
public sealed class ClientCommandsTests
{
    /// <summary>Checks client run requires a profile id.</summary>
    [TestMethod]
    public void ClientRunRequiresProfile()
    {
        ParseResult result = ClientCommands.CreateClientCommand().Parse("run");

        Assert.AreNotEqual(0, result.Errors.Count);
    }

    /// <summary>Checks client run accepts a profile id.</summary>
    [TestMethod]
    public void ClientRunAcceptsProfile()
    {
        ParseResult result = ClientCommands.CreateClientCommand().Parse("run test-game");

        Assert.HasCount(0, result.Errors);
    }
}

#pragma warning restore CA1416
