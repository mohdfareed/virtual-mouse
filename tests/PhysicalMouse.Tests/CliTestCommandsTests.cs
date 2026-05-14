using System;
using System.Linq;
using System.Threading;

namespace PhysicalMouse.Tests;

/// <summary>Tests for benchmark CLI helpers.</summary>
[TestClass]
public sealed class CliTestCommandsTests
{
    /// <summary>Checks benchmark mode names.</summary>
    [TestMethod]
    public void CreateBenchCommandIncludesBoundaryModes()
    {
        string[] names = [.. CliTestCommands.CreateBenchCommand().Subcommands.Select(command => command.Name)];

        CollectionAssert.Contains(names, "bridge");
        CollectionAssert.Contains(names, "raw");
        CollectionAssert.Contains(names, "all");
    }

    /// <summary>Checks that the benchmark path uses VIIPER report mapping.</summary>
    [TestMethod]
    public void BenchmarkSourceToViiperApiUsesViiperRangeChecks()
    {
        MouseReport report = new(MouseButtons.None, short.MaxValue + 1, 0, 0);

        try
        {
            _ = CliTestCommands.BenchmarkSourceToViiperApi(report, 1, CancellationToken.None);
            Assert.Fail("Expected OverflowException.");
        }
        catch (OverflowException)
        {
        }
    }

    /// <summary>Checks benchmark sample shape.</summary>
    [TestMethod]
    public void BenchmarkSourceToViiperApiRecordsSamples()
    {
        MouseReport report = new(MouseButtons.None, 1, 0, 0);

        CliTestCommands.BenchmarkResult result =
            CliTestCommands.BenchmarkSourceToViiperApi(report, 3, CancellationToken.None);

        Assert.AreEqual(3, result.Count);
        Assert.HasCount(3, result.Samples);
        Assert.IsGreaterThanOrEqualTo(0, result.TotalElapsed);
    }
}
