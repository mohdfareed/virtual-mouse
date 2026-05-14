using System;
using System.CommandLine;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using PhysicalMouse;
using PhysicalMouse.Viiper;
using VirtualMouse;
using VirtualMouse.RawInput;
using ViiperMouseInput = global::Viiper.Client.Devices.Mouse.MouseInput;

internal static class CliTestCommands
{
    private const int BenchmarkCount = 10_000;
    private const int RawBenchmarkCount = 1_000;
    private const int BenchmarkWarmup = 1_000;

    // MARK: Commands
    // ========================================================================

    internal static Command CreateBenchCommand()
    {
        Command command = new("bench", "Measure repository mouse forwarding cost.");
        Option<int?> countOption = CreateCountOption(BenchmarkCount);

        command.Options.Add(countOption);
        AddPositiveValidator(countOption, "--count");
        command.Subcommands.Add(CreateBridgeBenchCommand());
        command.Subcommands.Add(CreateRawBenchCommand());
        command.Subcommands.Add(CreateAllBenchCommand());

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int count = parseResult.GetValue(countOption) ?? BenchmarkCount;
            await RunBridgeBenchAsync(count, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Helpers
    // ========================================================================

    private static Command CreateBridgeBenchCommand()
    {
        Command command = new("bridge", "Measure callback to VIIPER API boundary.");
        Option<int?> countOption = CreateCountOption(BenchmarkCount);

        command.Options.Add(countOption);
        AddPositiveValidator(countOption, "--count");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int count = parseResult.GetValue(countOption) ?? BenchmarkCount;
            await RunBridgeBenchAsync(count, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateRawBenchCommand()
    {
        Command command = new("raw", "Measure Raw Input read/decode to callback boundary.");
        Option<int?> countOption = CreateCountOption(RawBenchmarkCount);

        command.Options.Add(countOption);
        AddPositiveValidator(countOption, "--count");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int count = parseResult.GetValue(countOption) ?? RawBenchmarkCount;
            await RunRawBenchAsync(count, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateAllBenchCommand()
    {
        Command command = new("all", "Measure Raw Input and bridge boundaries.");
        Option<int?> countOption = CreateCountOption(RawBenchmarkCount);

        command.Options.Add(countOption);
        AddPositiveValidator(countOption, "--count");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int count = parseResult.GetValue(countOption) ?? RawBenchmarkCount;
            await RunBridgeBenchAsync(count, cancellationToken).ConfigureAwait(false);
            await RunRawBenchAsync(count, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static async Task RunBridgeBenchAsync(int count, CancellationToken cancellationToken)
    {
        MouseReport report = new(MouseButtons.None, 1, 0, 0);
        BenchmarkResult benchmark = BenchmarkSourceToViiperApi(report, count, cancellationToken);
        await PrintBenchmarkAsync("bench bridge source->viiper-api", benchmark).ConfigureAwait(false);
    }

    private static async Task RunRawBenchAsync(int count, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            await Console.Error.WriteLineAsync("bench raw requires Windows.").ConfigureAwait(false);
            return;
        }

        await Console.Out.WriteLineAsync(
            $"bench raw: move the mouse until warmup {BenchmarkWarmup:N0} + reports {count:N0} are collected.")
            .ConfigureAwait(false);

        BenchmarkResult benchmark = await BenchmarkRawInputAsync(count, cancellationToken).ConfigureAwait(false);
        await PrintBenchmarkAsync("bench raw api->callback", benchmark).ConfigureAwait(false);
    }

    [SupportedOSPlatform("windows")]
    internal static async Task<BenchmarkResult> BenchmarkRawInputAsync(
        int count,
        CancellationToken cancellationToken)
    {
        using RawInputVirtualMouse input = await RawInputVirtualMouse
            .ConnectAsync(cancellationToken)
            .ConfigureAwait(false);
        using CancellationTokenSource runCancellation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);

        long[] samples = new long[count];
        int warmupCount = 0;
        int sampleCount = 0;
        long totalElapsed = 0;
        using CancellationTokenSource progressCancellation = CancellationTokenSource
            .CreateLinkedTokenSource(runCancellation.Token);
        Task progressTask = PrintRawProgressAsync(
            () => Volatile.Read(ref warmupCount),
            () => Volatile.Read(ref sampleCount),
            count,
            progressCancellation.Token);

        try
        {
            input.Run(HandleInput, HandleTiming, runCancellation.Token);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            await progressCancellation.CancelAsync().ConfigureAwait(false);
            await progressTask.ConfigureAwait(false);
        }

        return sampleCount < count
            ? throw new InvalidOperationException("Raw Input benchmark stopped before collecting enough reports.")
            : new BenchmarkResult(count, totalElapsed, samples, -1);

        static void HandleInput(in MouseInput input)
        {
            _ = input;
        }

        void HandleTiming(long startedTimestamp, long emittedTimestamp)
        {
            if (warmupCount < BenchmarkWarmup)
            {
                warmupCount++;
                return;
            }

            if (sampleCount >= count)
            {
                return;
            }

            long elapsed = emittedTimestamp - startedTimestamp;
            samples[sampleCount] = elapsed;
            totalElapsed += elapsed;
            sampleCount++;

            if (sampleCount == count)
            {
                runCancellation.Cancel();
            }
        }
    }

    internal static BenchmarkResult BenchmarkSourceToViiperApi(
        MouseReport report,
        int count,
        CancellationToken cancellationToken)
    {
        MouseInput input = new(report, string.Empty);
        BenchmarkViiperApi viiperApi = new();
        BenchmarkMouse mouse = new(viiperApi);
        MouseInputHandler handler = HandleInput;

        for (int i = 0; i < BenchmarkWarmup; i++)
        {
            handler(in input);
        }

        long[] samples = new long[count];
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        long totalElapsed = 0;

        for (int i = 0; i < count; i++)
        {
            viiperApi.Reset();
            long start = Stopwatch.GetTimestamp();
            handler(in input);
            long elapsed = viiperApi.LastReceivedTimestamp - start;
            samples[i] = elapsed;
            totalElapsed += elapsed;
        }

        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;
        return new BenchmarkResult(count, totalElapsed, samples, allocatedBytes);

        void HandleInput(in MouseInput source)
        {
            if (!source.Report.IsEmpty)
            {
                SendSynchronously(mouse, source.Report, cancellationToken);
            }
        }
    }

    private static Option<int?> CreateCountOption(int defaultCount)
    {
        return new Option<int?>("--count")
        {
            Description = $"Measured reports. Default: {defaultCount}.",
        };
    }

    private static void SendSynchronously(BenchmarkMouse mouse, MouseReport report, CancellationToken cancellationToken)
    {
        ValueTask sendTask = mouse.SendAsync(report, cancellationToken);
        if (sendTask.IsCompleted)
        {
            sendTask.GetAwaiter().GetResult();
            return;
        }

        sendTask.AsTask().GetAwaiter().GetResult();
    }

    private static async Task PrintBenchmarkAsync(string title, BenchmarkResult result)
    {
        int count = result.Count;
        long[] samples = result.Samples;
        Array.Sort(samples);

        double totalMs = ToMilliseconds(result.TotalElapsed);
        double sendsPerSecond = count / (totalMs / 1000.0);
        double mouseRateMultiple = sendsPerSecond / 1000.0;
        double averageMs = totalMs / count;
        double middleMs = ToMilliseconds(samples[count / 2]);
        double slow95Ms = ToMilliseconds(samples[(int)Math.Clamp(Math.Ceiling(count * 0.95) - 1, 0, count - 1)]);
        double slow99Ms = ToMilliseconds(samples[(int)Math.Clamp(Math.Ceiling(count * 0.99) - 1, 0, count - 1)]);
        double maxMs = ToMilliseconds(samples[count - 1]);
        double allocatedBytesPerSend = result.AllocatedBytes / (double)count;

        await Console.Out.WriteLineAsync(title).ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"  reports  {count:N0}  warmup {BenchmarkWarmup:N0}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"  rate     {sendsPerSecond:N0}/s  ({mouseRateMultiple:N0}x 1000 Hz)").ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            $"  time     avg {ToMicroseconds(averageMs):F3} us  " +
            $"50% {ToMicroseconds(middleMs):F3} us  " +
            $"95% {ToMicroseconds(slow95Ms):F3} us  " +
            $"99% {ToMicroseconds(slow99Ms):F3} us  " +
            $"max {ToMicroseconds(maxMs):F3} us").ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            result.AllocatedBytes < 0
                ? "  alloc    n/a"
                : $"  alloc    {allocatedBytesPerSend:F1} B/report").ConfigureAwait(false);
    }

    private static async Task PrintRawProgressAsync(
        Func<int> getWarmupCount,
        Func<int> getSampleCount,
        int count,
        CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                int warmupCount = Math.Min(getWarmupCount(), BenchmarkWarmup);
                int sampleCount = Math.Min(getSampleCount(), count);
                await Console.Out.WriteLineAsync(
                    $"  progress warmup {warmupCount:N0}/{BenchmarkWarmup:N0}  reports {sampleCount:N0}/{count:N0}")
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
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

    private static double ToMicroseconds(double milliseconds)
    {
        return milliseconds * 1000.0;
    }

    private sealed class BenchmarkViiperApi
    {
        private int checksum;

        public long LastReceivedTimestamp { get; private set; }

        public void Reset()
        {
            LastReceivedTimestamp = 0;
        }

        public void Send(MouseReport report, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ViiperMouseInput input = ViiperPhysicalMouse.MapReport(report);

            LastReceivedTimestamp = Stopwatch.GetTimestamp();
            checksum ^= input.Dx;
            checksum ^= input.Dy << 8;
            checksum ^= input.Wheel << 16;
            checksum ^= input.Buttons << 24;
            _ = checksum;
        }
    }

    private sealed class BenchmarkMouse(BenchmarkViiperApi viiperApi)
    {
        public ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken)
        {
            viiperApi.Send(report, cancellationToken);
            return ValueTask.CompletedTask;
        }
    }

    internal readonly record struct BenchmarkResult(
        int Count,
        long TotalElapsed,
        long[] Samples,
        long AllocatedBytes);
}
