using System;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PhysicalMouse;
using PhysicalMouse.Viiper;

internal static class CliDiagnosticsCommands
{
    // MARK: Commands
    // ========================================================================

    internal static Command CreateSmokeCommand()
    {
        Command command = new("smoke", "Move out and back with a pause.");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            const int distance = 300;
            const int pauseMs = 1000;

            _ = await CliConnection.ExecuteAsync(
                async (mouse, ct) =>
                {
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);
                    await mouse.SendAsync(new MouseReport(MouseButtons.None, distance, 0, 0), ct).ConfigureAwait(false);
                    await Task.Delay(pauseMs, ct).ConfigureAwait(false);
                    await mouse.SendAsync(new MouseReport(MouseButtons.None, -distance, 0, 0), ct).ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Smoke OK. Moved +{distance}, waited {pauseMs} ms, then moved -{distance}.").ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

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

    internal static Command CreateSmoothCommand()
    {
        Command command = new("smooth", "Draw a slow circle for visual checking.");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            const int radius = 80;
            const int steps = 240;
            const int durationMs = 4000;

            _ = await CliConnection.ExecuteAsync(
                async (mouse, ct) =>
                {
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);
                    await DrawCircleAsync(mouse, radius, steps, durationMs, ct).ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Smooth OK. Drew a circle with radius {radius}, {steps} steps, over {durationMs} ms.").ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Helpers
    // ========================================================================

    private static async Task DrawCircleAsync(
        ViiperPhysicalMouse mouse,
        int radius,
        int steps,
        int durationMs,
        CancellationToken cancellationToken)
    {
        double stepDelayMs = durationMs / (double)steps;
        double previousX = 0;
        double previousY = 0;

        for (int step = 1; step <= steps; step++)
        {
            double angle = step * (Math.PI * 2.0 / steps);
            double targetX = radius * Math.Cos(angle);
            double targetY = radius * Math.Sin(angle);
            int dx = (int)Math.Round(targetX - previousX);
            int dy = (int)Math.Round(targetY - previousY);

            if (dx != 0 || dy != 0)
            {
                await mouse.SendAsync(new MouseReport(MouseButtons.None, dx, dy, 0), cancellationToken).ConfigureAwait(false);
            }

            previousX += dx;
            previousY += dy;

            if (step < steps)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(stepDelayMs), cancellationToken).ConfigureAwait(false);
            }
        }

        int returnDx = -(int)Math.Round(previousX);
        int returnDy = -(int)Math.Round(previousY);
        if (returnDx != 0 || returnDy != 0)
        {
            await mouse.SendAsync(new MouseReport(MouseButtons.None, returnDx, returnDy, 0), cancellationToken).ConfigureAwait(false);
        }
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
