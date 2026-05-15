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
    private const double BridgeP99FailureMicroseconds = 10.0;

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
        FailBridgeBenchmarkIfSlow(benchmark);
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
        BenchmarkTiming warmupTiming = new(null);
        BenchmarkViiperApi warmupViiperApi = new(warmupTiming);
        using BenchmarkPhysicalMouse warmupMouse = new(warmupViiperApi);
        using BenchmarkVirtualMouse warmupSource = new(input, BenchmarkWarmup, warmupTiming);
        warmupSource.RunTo(warmupMouse, cancellationToken);

        long[] samples = new long[count];
        BenchmarkTiming timing = new(samples);
        BenchmarkViiperApi viiperApi = new(timing);
        using BenchmarkPhysicalMouse mouse = new(viiperApi);
        using BenchmarkVirtualMouse source = new(input, count, timing);
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        source.RunTo(mouse, cancellationToken);

        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;
        return new BenchmarkResult(count, timing.TotalElapsed, samples, allocatedBytes);
    }

    private static Option<int?> CreateCountOption(int defaultCount)
    {
        return new Option<int?>("--count")
        {
            Description = $"Measured reports. Default: {defaultCount}.",
        };
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

    private static void FailBridgeBenchmarkIfSlow(BenchmarkResult result)
    {
        long[] samples = result.Samples;
        Array.Sort(samples);

        double slow99Ms = ToMilliseconds(samples[(int)Math.Clamp(Math.Ceiling(result.Count * 0.99) - 1, 0, result.Count - 1)]);
        double slow99Us = ToMicroseconds(slow99Ms);
        if (slow99Us > BridgeP99FailureMicroseconds)
        {
            throw new InvalidOperationException(
                $"bench bridge p99 {slow99Us:F3} us exceeded {BridgeP99FailureMicroseconds:F3} us.");
        }
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

    private sealed class BenchmarkTiming(long[]? samples)
    {
        private int sampleIndex;

        public long CurrentStartTimestamp { get; private set; }

        public long TotalElapsed { get; private set; }

        public void BeginReport()
        {
            CurrentStartTimestamp = Stopwatch.GetTimestamp();
        }

        public void EndReport()
        {
            long elapsed = Stopwatch.GetTimestamp() - CurrentStartTimestamp;
            if (samples is null)
            {
                return;
            }

            samples[sampleIndex++] = elapsed;
            TotalElapsed += elapsed;
        }
    }

    private sealed class BenchmarkVirtualMouse(
        MouseInput input,
        int count,
        BenchmarkTiming timing) : IVirtualMouse, IDisposable
    {
        public bool IsConnected => true;

        public void Run(MouseInputHandler handler, CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                timing.BeginReport();
                handler(in input);
            }
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class BenchmarkViiperApi(BenchmarkTiming timing)
    {
        private int checksum;

        public void Send(MouseReport report, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ViiperMouseInput input = ViiperPhysicalMouse.MapReport(report);

            timing.EndReport();
            checksum ^= input.Dx;
            checksum ^= input.Dy << 8;
            checksum ^= input.Wheel << 16;
            checksum ^= input.Buttons << 24;
            _ = checksum;
        }
    }

    private sealed class BenchmarkPhysicalMouse(BenchmarkViiperApi viiperApi) : IPhysicalMouse, IDisposable
    {
        public bool IsConnected => true;

        public bool FilterInput(in MouseInput input)
        {
            _ = input;
            return true;
        }

        public ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
        {
            viiperApi.Send(report, cancellationToken);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    internal readonly record struct BenchmarkResult(
        int Count,
        long TotalElapsed,
        long[] Samples,
        long AllocatedBytes);
}
