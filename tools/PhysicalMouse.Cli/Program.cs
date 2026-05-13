using System;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhysicalMouse;
using PhysicalMouse.Viiper;

internal static class Program
{
    // MARK: Entry
    // ========================================================================

    private static async Task<int> Main(string[] args)
    {
        RootCommand root = new("CLI for smoke tests and send benchmarks.");
        root.Subcommands.Add(CreateConnectCommand());
        root.Subcommands.Add(CreateMoveCommand());
        root.Subcommands.Add(CreateClickCommand());
        root.Subcommands.Add(CreateWheelCommand());
        root.Subcommands.Add(CreateSmokeCommand());
        root.Subcommands.Add(CreateBenchCommand());

        return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
    }

    // MARK: Commands
    // ========================================================================

    private static Command CreateConnectCommand()
    {
        Command command = new("connect", "Connect to VIIPER and print the connected IDs.");
        ConnectionOptions options = AddConnectionOptions(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            _ = await ExecuteWithMouseAsync(
                parseResult,
                options,
                async (mouse, _) =>
                {
                    await PrintConnectionAsync(mouse).ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateMoveCommand()
    {
        Command command = new("move", "Send one relative move report.");
        Option<int> dxOption = new("--dx")
        {
            Description = "Horizontal delta.",
        };

        Option<int> dyOption = new("--dy")
        {
            Description = "Vertical delta.",
        };

        command.Options.Add(dxOption);
        command.Options.Add(dyOption);
        ConnectionOptions options = AddConnectionOptions(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int dx = parseResult.GetValue(dxOption);
            int dy = parseResult.GetValue(dyOption);

            _ = await ExecuteWithMouseAsync(
                parseResult,
                options,
                async (mouse, ct) =>
                {
                    await mouse.SendAsync(new MouseReport(MouseButtons.None, dx, dy, 0), ct).ConfigureAwait(false);
                    await PrintConnectionAsync(mouse).ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateClickCommand()
    {
        Command command = new("click", "Send a button press and release.");
        Option<string> buttonOption = new("--button")
        {
            Description = "Button to click.",
            Required = true,
        };

        command.Options.Add(buttonOption);
        ConnectionOptions options = AddConnectionOptions(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            MouseButtons button = ParseButton(parseResult.GetValue(buttonOption) ?? throw new InvalidOperationException("Missing required --button value."));

            _ = await ExecuteWithMouseAsync(
                parseResult,
                options,
                async (mouse, ct) =>
                {
                    await mouse.SendAsync(new MouseReport(button, 0, 0, 0), ct).ConfigureAwait(false);
                    await mouse.SendAsync(MouseReport.Empty, ct).ConfigureAwait(false);
                    await PrintConnectionAsync(mouse).ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateWheelCommand()
    {
        Command command = new("wheel", "Send one vertical wheel report.");
        Option<int> deltaOption = new("--delta")
        {
            Description = "Wheel delta.",
            Required = true,
        };

        command.Options.Add(deltaOption);
        ConnectionOptions options = AddConnectionOptions(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int delta = parseResult.GetValue(deltaOption);

            _ = await ExecuteWithMouseAsync(
                parseResult,
                options,
                async (mouse, ct) =>
                {
                    await mouse.SendAsync(new MouseReport(MouseButtons.None, 0, 0, delta), ct).ConfigureAwait(false);
                    await PrintConnectionAsync(mouse).ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateSmokeCommand()
    {
        Command command = new("smoke", "Do a simple visible move out and back.");
        Option<int> distanceOption = new("--distance")
        {
            Description = "Distance to move before returning.",
            DefaultValueFactory = _ => 50,
        };

        command.Options.Add(distanceOption);
        ConnectionOptions options = AddConnectionOptions(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int distance = parseResult.GetValue(distanceOption);

            _ = await ExecuteWithMouseAsync(
                parseResult,
                options,
                async (mouse, ct) =>
                {
                    await PrintConnectionAsync(mouse).ConfigureAwait(false);
                    await mouse.SendAsync(new MouseReport(MouseButtons.None, distance, 0, 0), ct).ConfigureAwait(false);
                    await mouse.SendAsync(new MouseReport(MouseButtons.None, -distance, 0, 0), ct).ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Smoke OK. Moved +{distance} then -{distance}.").ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateBenchCommand()
    {
        Command command = new("bench", "Measure send-path cost over many reports.");
        Option<int> countOption = new("--count")
        {
            Description = "Measured send count.",
            DefaultValueFactory = _ => 10_000,
        };

        Option<int> warmupOption = new("--warmup")
        {
            Description = "Warmup send count.",
            DefaultValueFactory = _ => 1_000,
        };

        Option<int> dxOption = new("--dx")
        {
            Description = "Horizontal delta per report.",
            DefaultValueFactory = _ => 1,
        };

        Option<int> dyOption = new("--dy")
        {
            Description = "Vertical delta per report.",
            DefaultValueFactory = _ => 0,
        };

        Option<int> wheelOption = new("--wheel")
        {
            Description = "Wheel delta per report.",
            DefaultValueFactory = _ => 0,
        };

        command.Options.Add(countOption);
        command.Options.Add(warmupOption);
        command.Options.Add(dxOption);
        command.Options.Add(dyOption);
        command.Options.Add(wheelOption);
        ConnectionOptions options = AddConnectionOptions(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int count = parseResult.GetValue(countOption);
            int warmup = parseResult.GetValue(warmupOption);
            int dx = parseResult.GetValue(dxOption);
            int dy = parseResult.GetValue(dyOption);
            int wheel = parseResult.GetValue(wheelOption);
            MouseReport report = new(MouseButtons.None, dx, dy, wheel);

            _ = await ExecuteWithMouseAsync(
                parseResult,
                options,
                async (mouse, ct) =>
                {
                    await PrintConnectionAsync(mouse).ConfigureAwait(false);

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

    // MARK: Output
    // ========================================================================

    private static async Task PrintConnectionAsync(ViiperPhysicalMouse mouse)
    {
        await Console.Out.WriteLineAsync($"Connected: {mouse.IsConnected}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"BusId: {mouse.BusId?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"DeviceId: {mouse.DeviceId ?? "<unknown>"}").ConfigureAwait(false);
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

    // MARK: Helpers
    // ========================================================================

    private static ConnectionOptions AddConnectionOptions(Command command)
    {
        Option<string> hostOption = new("--host")
        {
            Description = "Host name or IP address.",
            DefaultValueFactory = _ => "127.0.0.1",
        };

        Option<int> portOption = new("--port")
        {
            Description = "TCP port.",
            DefaultValueFactory = _ => 3242,
        };

        Option<string> passwordOption = new("--password")
        {
            Description = "Server password.",
            DefaultValueFactory = _ => string.Empty,
        };

        Option<uint?> busIdOption = new("--bus-id")
        {
            Description = "Bus to use, if known.",
        };

        Option<string?> deviceIdOption = new("--device-id")
        {
            Description = "Device to use, if known.",
        };

        Option<bool> removeCreatedDeviceOnDisposeOption = new("--remove-created-device-on-dispose")
        {
            Description = "Remove a newly created device on dispose.",
        };

        Option<string?> logLevelOption = new("--log-level")
        {
            Description = "trace, debug, information, warning, error, critical, or none.",
        };

        command.Options.Add(hostOption);
        command.Options.Add(portOption);
        command.Options.Add(passwordOption);
        command.Options.Add(busIdOption);
        command.Options.Add(deviceIdOption);
        command.Options.Add(removeCreatedDeviceOnDisposeOption);
        command.Options.Add(logLevelOption);

        deviceIdOption.Validators.Add(result =>
        {
            uint? busId = result.GetValue(busIdOption);
            string? deviceId = result.GetValue(deviceIdOption);
            if (!string.IsNullOrWhiteSpace(deviceId) && !busId.HasValue)
            {
                result.AddError("--device-id requires --bus-id.");
            }
        });

        logLevelOption.Validators.Add(result =>
        {
            string? value = result.GetValue(logLevelOption);
            if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) && !Enum.TryParse<LogLevel>(value, true, out _))
            {
                result.AddError("Invalid --log-level value.");
            }
        });

        return new ConnectionOptions(
            hostOption,
            portOption,
            passwordOption,
            busIdOption,
            deviceIdOption,
            removeCreatedDeviceOnDisposeOption,
            logLevelOption);
    }

    private static async Task<int> ExecuteWithMouseAsync(
        ParseResult parseResult,
        ConnectionOptions options,
        Func<ViiperPhysicalMouse, CancellationToken, Task<int>> action,
        CancellationToken cancellationToken)
    {
        using ILoggerFactory? loggerFactory = CreateLoggerFactory(parseResult.GetValue(options.LogLevelOption));
        ILogger? logger = loggerFactory?.CreateLogger("PhysicalMouse.Cli");
        ViiperOptions viiperOptions = CreateViiperOptions(parseResult, options, logger);
        ViiperPhysicalMouse mouse = await ViiperPhysicalMouse.ConnectAsync(viiperOptions, cancellationToken).ConfigureAwait(false);

        try
        {
            return await action(mouse, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await mouse.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static ViiperOptions CreateViiperOptions(ParseResult parseResult, ConnectionOptions options, ILogger? logger)
    {
        return new ViiperOptions
        {
            Host = parseResult.GetValue(options.HostOption) ?? "127.0.0.1",
            Port = parseResult.GetValue(options.PortOption),
            Password = parseResult.GetValue(options.PasswordOption) ?? string.Empty,
            BusId = parseResult.GetValue(options.BusIdOption),
            DeviceId = parseResult.GetValue(options.DeviceIdOption),
            RemoveCreatedDeviceOnDispose = parseResult.GetValue(options.RemoveCreatedDeviceOnDisposeOption),
            Logger = logger,
        };
    }

    private static ILoggerFactory? CreateLoggerFactory(string? logLevel)
    {
        if (string.IsNullOrWhiteSpace(logLevel) || string.Equals(logLevel, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        LogLevel parsedLogLevel = Enum.Parse<LogLevel>(logLevel, ignoreCase: true);
        return LoggerFactory.Create(builder =>
        {
            _ = builder.SetMinimumLevel(parsedLogLevel);
            _ = builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss.fff ";
            });
        });
    }

    private static MouseButtons ParseButton(string value)
    {
        return string.Equals(value, "left", StringComparison.OrdinalIgnoreCase) ? MouseButtons.Left :
            string.Equals(value, "right", StringComparison.OrdinalIgnoreCase) ? MouseButtons.Right :
            string.Equals(value, "middle", StringComparison.OrdinalIgnoreCase) ? MouseButtons.Middle :
            string.Equals(value, "back", StringComparison.OrdinalIgnoreCase) ? MouseButtons.Back :
            string.Equals(value, "forward", StringComparison.OrdinalIgnoreCase) ? MouseButtons.Forward :
            throw new ArgumentException($"Unknown button '{value}'.");
    }

    private static double ToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static double ToMicroseconds(long ticks)
    {
        return ticks * 1_000_000.0 / Stopwatch.Frequency;
    }

    private readonly record struct ConnectionOptions(
        Option<string> HostOption,
        Option<int> PortOption,
        Option<string> PasswordOption,
        Option<uint?> BusIdOption,
        Option<string?> DeviceIdOption,
        Option<bool> RemoveCreatedDeviceOnDisposeOption,
        Option<string?> LogLevelOption);
}
