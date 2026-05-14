using System;
using System.CommandLine;
using System.Diagnostics;
using System.Threading.Tasks;
using PhysicalMouse;

internal static class CliTestCommands
{
    private const int BenchmarkCount = 10_000;
    private const int BenchmarkWarmup = 1_000;

    // MARK: Commands
    // ========================================================================

    internal static Command CreateBenchCommand()
    {
        Command command = new("bench", "Measure VIIPER mouse send cost.");
        Option<int?> countOption = new("--count")
        {
            Description = $"Measured sends. Default: {BenchmarkCount}.",
        };

        command.Options.Add(countOption);
        AddPositiveValidator(countOption, "--count");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int count = parseResult.GetValue(countOption) ?? BenchmarkCount;
            MouseReport report = new(MouseButtons.None, 1, 0, 0);

            _ = await CliConnection.ExecuteAsync(
                async (mouse, ct) =>
                {
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);

                    for (int i = 0; i < BenchmarkWarmup; i++)
                    {
                        await mouse.SendAsync(report, ct).ConfigureAwait(false);
                    }

                    long[] samples = new long[count];
                    long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
                    long totalStart = Stopwatch.GetTimestamp();

                    for (int i = 0; i < count; i++)
                    {
                        long start = Stopwatch.GetTimestamp();
                        await mouse.SendAsync(report, ct).ConfigureAwait(false);
                        samples[i] = Stopwatch.GetTimestamp() - start;
                    }

                    long totalElapsed = Stopwatch.GetTimestamp() - totalStart;
                    long allocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;
                    await PrintBenchmarkAsync(count, totalElapsed, samples, allocatedBytes).ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Helpers
    // ========================================================================

    private static async Task PrintBenchmarkAsync(
        int count,
        long totalElapsed,
        long[] samples,
        long allocatedBytes)
    {
        Array.Sort(samples);

        double totalMs = ToMilliseconds(totalElapsed);
        double sendsPerSecond = count / (totalMs / 1000.0);
        double averageMs = totalMs / count;
        double middleMs = ToMilliseconds(samples[count / 2]);
        double slow95Ms = ToMilliseconds(samples[(int)Math.Clamp(Math.Ceiling(count * 0.95) - 1, 0, count - 1)]);
        double slow99Ms = ToMilliseconds(samples[(int)Math.Clamp(Math.Ceiling(count * 0.99) - 1, 0, count - 1)]);
        double maxMs = ToMilliseconds(samples[count - 1]);
        double allocatedBytesPerSend = allocatedBytes / (double)count;

        await Console.Out.WriteLineAsync("bench").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"  samples     {count:N0} | warmup {BenchmarkWarmup:N0}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"  throughput  {sendsPerSecond:N0} Hz").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"  send        avg {averageMs:F3} ms").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"              50% {middleMs:F3} ms").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"              95% {slow95Ms:F3} ms").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"              99% {slow99Ms:F3} ms").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"              max {maxMs:F3} ms").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"  allocation  {allocatedBytesPerSend:F1} B/send").ConfigureAwait(false);
    }

    private static void AddPositiveValidator(Option<int?> option, string name)
    {
        option.Validators.Add(result =>
        {
            int? value = result.GetValue(option);
            if (value.HasValue && value.Value <= 0)
            {
                result.AddError($"{name} must be greater than 0.");
            }
        });
    }

    private static double ToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }
}
