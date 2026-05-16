using System.Collections.Generic;

namespace Cli.Tools.Benchmarks;

/// <summary>Benchmark input source kind.</summary>
internal enum ForwardingBenchmarkInput
{
    /// <summary>Windows Raw Input mouse.</summary>
    Raw,

    /// <summary>SDL gamepad.</summary>
    Sdl,
}

/// <summary>Benchmark output transport kind.</summary>
internal enum ForwardingBenchmarkOutput
{
    /// <summary>VIIPER output.</summary>
    Viiper,

    /// <summary>Teensy output.</summary>
    Teensy,
}

/// <summary>Benchmark progress.</summary>
/// <param name="WarmupCount">Collected warmup reports.</param>
/// <param name="WarmupTarget">Warmup target.</param>
/// <param name="SampleCount">Collected measured reports.</param>
/// <param name="SampleTarget">Measured report target.</param>
internal readonly record struct ForwardingBenchmarkProgress(
    int WarmupCount,
    int WarmupTarget,
    int SampleCount,
    int SampleTarget);

/// <summary>Benchmark measurement.</summary>
/// <param name="Count">Measured report count.</param>
/// <param name="TotalElapsed">Total elapsed timestamp ticks.</param>
/// <param name="Samples">Per-report elapsed timestamp ticks.</param>
/// <param name="AllocatedBytes">Allocated bytes, or -1 when not measured.</param>
internal readonly record struct ForwardingBenchmarkMeasurement(
    int Count,
    long TotalElapsed,
    IReadOnlyList<long> Samples,
    long AllocatedBytes);

/// <summary>Named benchmark measurement.</summary>
/// <param name="Title">Measurement title.</param>
/// <param name="Measurement">Measurement values.</param>
internal readonly record struct ForwardingBenchmarkResult(
    string Title,
    ForwardingBenchmarkMeasurement Measurement);
