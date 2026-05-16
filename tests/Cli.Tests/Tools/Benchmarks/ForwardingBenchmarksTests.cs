using System;
using System.Threading;
using Cli.Tools.Benchmarks;

namespace Cli.Tests.Tools.Benchmarks;

/// <summary>Tests forwarding benchmark mechanics.</summary>
[TestClass]
public sealed class ForwardingBenchmarksTests
{
    /// <summary>Checks that the benchmark path uses VIIPER report mapping.</summary>
    [TestMethod]
    public void BenchmarkSourceToViiperApiUsesViiperRangeChecks()
    {
        MouseReport report = new(MouseButtons.None, short.MaxValue + 1, 0, 0);

        try
        {
            _ = ForwardingBenchmarks.BenchmarkSourceToViiperApi(report, 1, CancellationToken.None);
            Assert.Fail("Expected OverflowException.");
        }
        catch (OverflowException)
        {
        }
    }

    /// <summary>Checks mouse bridge benchmark sample shape.</summary>
    [TestMethod]
    public void BenchmarkSourceToViiperApiRecordsSamples()
    {
        MouseReport report = new(MouseButtons.None, 1, 0, 0);

        ForwardingBenchmarkMeasurement result =
            ForwardingBenchmarks.BenchmarkSourceToViiperApi(report, 3, CancellationToken.None);

        Assert.AreEqual(3, result.Count);
        Assert.HasCount(3, result.Samples);
        Assert.IsGreaterThanOrEqualTo(0, result.TotalElapsed);
    }

    /// <summary>Checks xpad bridge benchmark sample shape.</summary>
    [TestMethod]
    public void BenchmarkGamepadToViiperApiRecordsSamples()
    {
        GamepadState state = new(GamepadButtons.South, 1, -2, 3, -4, 32767, 0, default);

        ForwardingBenchmarkMeasurement result =
            ForwardingBenchmarks.BenchmarkGamepadToViiperApi(state, 3, CancellationToken.None);

        Assert.AreEqual(3, result.Count);
        Assert.HasCount(3, result.Samples);
        Assert.IsGreaterThanOrEqualTo(0, result.TotalElapsed);
    }
}
