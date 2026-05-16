using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Inputs;
using Outputs;
using Outputs.Viiper;
using ViiperMouseInput = global::Viiper.Client.Devices.Mouse.MouseInput;
using ViiperXbox360Input = global::Viiper.Client.Devices.Xbox360.Xbox360Input;

namespace Cli.Tools.Benchmarks;

internal static partial class ForwardingBenchmarks
{
    private static void ThrowIfInvalidCount(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than 0.");
        }
    }

    private static void CollectSample(
        long startedTimestamp,
        long emittedTimestamp,
        int count,
        long[] samples,
        ref int warmupCount,
        ref int sampleCount,
        ref long totalElapsed,
        CancellationTokenSource runCancellation)
    {
        if (warmupCount < WarmupCount)
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

    private sealed class BenchmarkMouseSource(
        MouseInput input,
        int count,
        BenchmarkTiming timing) : IMouseInputSource, IDisposable
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

    private sealed class BenchmarkGamepadSource(
        GamepadState state,
        int count,
        BenchmarkTiming timing) : IGamepadInputSource, IDisposable
    {
        public bool IsConnected => true;

        public void Run(GamepadInputHandler handler, CancellationToken cancellationToken = default)
        {
            GamepadInput input = new(state, string.Empty);
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

    private sealed class BenchmarkViiperMouseApi(BenchmarkTiming timing)
    {
        private int checksum;

        public void Send(MouseReport report, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ViiperMouseInput input = ViiperMouseOutput.MapReport(report);

            timing.EndReport();
            checksum ^= input.Dx;
            checksum ^= input.Dy << 8;
            checksum ^= input.Wheel << 16;
            checksum ^= input.Buttons << 24;
            _ = checksum;
        }
    }

    private sealed class BenchmarkMouseOutput(BenchmarkViiperMouseApi viiperApi) : IMouseOutput, IDisposable
    {
        public bool IsConnected => true;

        public bool FilterInput(string? deviceName)
        {
            _ = deviceName;
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

    private sealed class BenchmarkXbox360Output(BenchmarkTiming timing) : IXbox360Output, IDisposable
    {
        private int checksum;

        public bool IsConnected => true;

        public ValueTask SendAsync(Xbox360Report report, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ViiperXbox360Input input = ViiperXbox360Output.MapReport(report);

            timing.EndReport();
            checksum ^= unchecked((int)input.Buttons);
            checksum ^= input.Lt << 8;
            checksum ^= input.Rt << 16;
            checksum ^= input.Lx;
            checksum ^= input.Ly << 1;
            checksum ^= input.Rx << 2;
            checksum ^= input.Ry << 3;
            _ = checksum;
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
}
