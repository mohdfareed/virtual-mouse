using System;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PhysicalMouse;
using PhysicalMouse.Viiper;

internal static class CliTestCommands
{
    private static readonly int[] SmokePresetFps = [30, 60, 120, 240, 480, 960];

    private const int SmokeDistance = 500;
    private const int SmokeOneWayDurationMs = 250;
    private const int SmokeMinFps = 1;

    // MARK: Commands
    // ========================================================================

    internal static Command CreateBenchCommand()
    {
        Command command = new("bench", "Measure send-path cost over many reports.");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            const int count = 10_000;
            const int warmup = 1_000;
            const int dx = 1;
            const int dy = 0;
            const int wheel = 0;
            MouseReport report = new(MouseButtons.None, dx, dy, wheel);

            _ = await CliConnection.ExecuteAsync(
                async (mouse, ct) =>
                {
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);

                    for (int i = 0; i < warmup; i++)
                    {
                        await mouse.SendAsync(report, ct).ConfigureAwait(false);
                    }

                    long[] samples = new long[count];
                    long totalStart = Stopwatch.GetTimestamp();

                    for (int i = 0; i < count; i++)
                    {
                        long start = Stopwatch.GetTimestamp();
                        await mouse.SendAsync(report, ct).ConfigureAwait(false);
                        samples[i] = Stopwatch.GetTimestamp() - start;
                    }

                    long totalElapsed = Stopwatch.GetTimestamp() - totalStart;
                    await PrintBenchmarkAsync(count, warmup, totalElapsed, samples).ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static Command CreateSmokeCommand()
    {
        Command command = new("smoke", "Run a horizontal sweep for visual checking.");
        Option<int?> fpsOption = new("--fps")
        {
            Description = "Lock to one command rate and loop until Ctrl+C.",
        };

        command.Options.Add(fpsOption);

        fpsOption.Validators.Add(result =>
        {
            int? fps = result.GetValue(fpsOption);
            if (fps.HasValue && fps.Value < SmokeMinFps)
            {
                result.AddError("--fps must be greater than 0.");
            }
        });

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int? fps = parseResult.GetValue(fpsOption);

            _ = await CliConnection.ExecuteAsync(
                async (mouse, ct) =>
                {
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);

                    if (fps.HasValue)
                    {
                        await PrintSmokeProfileAsync(fps.Value, includeStopHint: true).ConfigureAwait(false);
                        while (!ct.IsCancellationRequested)
                        {
                            await RunSmokeAsync(mouse, fps.Value, ct).ConfigureAwait(false);
                        }

                        return 0;
                    }

                    foreach (int presetFps in SmokePresetFps)
                    {
                        await PrintSmokeProfileAsync(presetFps, includeStopHint: false).ConfigureAwait(false);
                        await RunSmokeAsync(mouse, presetFps, ct).ConfigureAwait(false);
                    }

                    await Console.Out.WriteLineAsync("Smoke OK.").ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Helpers
    // ========================================================================

    private static async Task PrintSmokeProfileAsync(int fps, bool includeStopHint)
    {
        string message = $"Commands/sec {fps}";
        if (includeStopHint)
        {
            message += ".\n\nPress Ctrl+C to stop.";
        }

        await Console.Out.WriteLineAsync(message).ConfigureAwait(false);
    }

    private static async Task RunSmokeAsync(
        ViiperPhysicalMouse mouse,
        int fps,
        CancellationToken cancellationToken)
    {
        int steps = Math.Max(1, (int)Math.Round(fps * (SmokeOneWayDurationMs / 1000.0)));
        await MoveLinearAsync(mouse, SmokeDistance, steps, cancellationToken).ConfigureAwait(false);
        await MoveLinearAsync(mouse, -SmokeDistance, steps, cancellationToken).ConfigureAwait(false);
    }

    private static async Task MoveLinearAsync(
        ViiperPhysicalMouse mouse,
        int totalDelta,
        int steps,
        CancellationToken cancellationToken)
    {
        int sent = 0;
        long start = Stopwatch.GetTimestamp();
        double durationTicks = Stopwatch.Frequency * (SmokeOneWayDurationMs / 1000.0);

        for (int step = 1; step <= steps; step++)
        {
            int target = (int)Math.Round(totalDelta * step / (double)steps);
            int delta = target - sent;
            if (delta != 0)
            {
                await mouse.SendAsync(new MouseReport(MouseButtons.None, delta, 0, 0), cancellationToken).ConfigureAwait(false);
                sent = target;
            }

            if (step < steps)
            {
                long nextDeadline = steps > 1
                    ? start + (long)Math.Round(step * durationTicks / (steps - 1))
                    : start;

                await DelayUntilAsync(nextDeadline, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task DelayUntilAsync(long deadline, CancellationToken cancellationToken)
    {
        long remaining = deadline - Stopwatch.GetTimestamp();
        if (remaining <= 0)
        {
            return;
        }

        await Task.Delay(
            TimeSpan.FromSeconds(remaining / (double)Stopwatch.Frequency),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task PrintBenchmarkAsync(int count, int warmup, long totalElapsed, long[] samples)
    {
        Array.Sort(samples);

        double totalMs = ToMilliseconds(totalElapsed);
        double averageUs = ToMicroseconds(totalElapsed) / count;
        double p50Us = ToMicroseconds(samples[count / 2]);
        double p95Us = ToMicroseconds(samples[(int)Math.Clamp(Math.Ceiling(count * 0.95) - 1, 0, count - 1)]);
        double p99Us = ToMicroseconds(samples[(int)Math.Clamp(Math.Ceiling(count * 0.99) - 1, 0, count - 1)]);
        double maxUs = ToMicroseconds(samples[count - 1]);
        double sendsPerSecond = count / (totalMs / 1000.0);

        await Console.Out.WriteLineAsync($"Warmup: {warmup}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"Count: {count}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"TotalMs: {totalMs:F3}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"AverageUs: {averageUs:F3}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"P50Us: {p50Us:F3}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"P95Us: {p95Us:F3}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"P99Us: {p99Us:F3}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"MaxUs: {maxUs:F3}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"SendsPerSecond: {sendsPerSecond:F0}").ConfigureAwait(false);
    }

    private static double ToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static double ToMicroseconds(long ticks)
    {
        return ticks * 1_000_000.0 / Stopwatch.Frequency;
    }
}
