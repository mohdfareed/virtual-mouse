using System;
using System.Threading;
using Hosting;
using Inputs;

namespace Cli.Tools.Benchmarks;

internal static partial class ForwardingBenchmarks
{
    /// <summary>Measures mouse source callback to VIIPER mouse API input mapping.</summary>
    public static ForwardingBenchmarkMeasurement BenchmarkSourceToViiperApi(
        MouseReport report,
        int count,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidCount(count);

        MouseInput input = new(report, string.Empty);
        BenchmarkTiming warmupTiming = new(null);
        BenchmarkViiperMouseApi warmupViiperApi = new(warmupTiming);
        using BenchmarkMouseOutput warmupMouse = new(warmupViiperApi);
        using BenchmarkMouseSource warmupSource = new(input, WarmupCount, warmupTiming);
        warmupSource.RunTo(warmupMouse, cancellationToken);

        long[] samples = new long[count];
        BenchmarkTiming timing = new(samples);
        BenchmarkViiperMouseApi viiperApi = new(timing);
        using BenchmarkMouseOutput mouse = new(viiperApi);
        using BenchmarkMouseSource source = new(input, count, timing);
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        source.RunTo(mouse, cancellationToken);

        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;
        return new ForwardingBenchmarkMeasurement(count, timing.TotalElapsed, samples, allocatedBytes);
    }

    /// <summary>Measures gamepad source callback to VIIPER Xbox 360 API input mapping.</summary>
    public static ForwardingBenchmarkMeasurement BenchmarkGamepadToViiperApi(
        GamepadState state,
        int count,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidCount(count);

        BenchmarkTiming warmupTiming = new(null);
        using BenchmarkGamepadSource warmupSource = new(state, WarmupCount, warmupTiming);
        using BenchmarkXbox360Output warmupOutput = new(warmupTiming);
        warmupSource.RunTo(warmupOutput, cancellationToken);

        long[] samples = new long[count];
        BenchmarkTiming timing = new(samples);
        using BenchmarkGamepadSource source = new(state, count, timing);
        using BenchmarkXbox360Output output = new(timing);
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        source.RunTo(output, cancellationToken);

        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;
        return new ForwardingBenchmarkMeasurement(count, timing.TotalElapsed, samples, allocatedBytes);
    }
}
