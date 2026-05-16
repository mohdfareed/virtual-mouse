using System.Linq;

namespace Cli.Tests;

/// <summary>Tests for benchmark CLI helpers.</summary>
[TestClass]
public sealed class BenchCommandsTests
{
    /// <summary>Checks benchmark command shape.</summary>
    [TestMethod]
    public void CreateBenchCommandHasInputAndOutputArguments()
    {
        System.CommandLine.Command command = BenchCommands.CreateBenchCommand();

        Assert.HasCount(0, command.Subcommands);
        Assert.HasCount(2, command.Arguments);
        CollectionAssert.Contains(command.Options.Select(option => option.Name).ToArray(), "--count");
    }
}
