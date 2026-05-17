using System;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hosting;

internal static class ClientCommands
{
    // MARK: Commands
    // ========================================================================

    internal static Command CreateClientCommand()
    {
        Command command = new("client", "Control a running forwarding host.");
        command.Subcommands.Add(CreateRunCommand());
        command.Subcommands.Add(CreateStateCommand(
            "emulation",
            "Control global emulation forwarding.",
            HostToggleKind.Emulation));
        command.Subcommands.Add(CreateStateCommand(
            "physical-motion",
            "Control global physical motion forwarding.",
            HostToggleKind.PhysicalMotion));
        return command;
    }

    private static Command CreateRunCommand()
    {
        Command command = new("run", "Open a client session and optionally enable a route until cancelled.");
        Option<ForwardingRouteKind?> routeOption = CliOptions.CreateRouteOption();
        command.Options.Add(routeOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ForwardingRouteKind? route = parseResult.GetValue(routeOption);
            ForwardingClientSession? session = await TryConnectAsync(route, cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                return;
            }

            await using (session.ConfigureAwait(false))
            {
                try
                {
                    string status = route.HasValue
                        ? $"route={ForwardingServer.GetRouteId(route.Value)} enabled=true"
                        : "route=none enabled=false";
                    await Console.Out.WriteLineAsync($"{status}. Ctrl+C to release.").ConfigureAwait(false);
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
            }
        });

        return command;
    }

    private static Command CreateStateCommand(
        string name,
        string description,
        HostToggleKind kind)
    {
        Command command = new(name, description);
        command.Subcommands.Add(CreateSetStateCommand("enable", enabled: true, kind));
        command.Subcommands.Add(CreateSetStateCommand("disable", enabled: false, kind));
        command.Subcommands.Add(CreateToggleStateCommand(kind));
        return command;
    }

    private static Command CreateSetStateCommand(string name, bool enabled, HostToggleKind kind)
    {
        Command command = new(name, $"{(enabled ? "Enable" : "Disable")} {DisplayDescription(kind)}.");

        command.SetAction(async (_, cancellationToken) =>
        {
            await SetHostStateAsync(kind, enabled, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateToggleStateCommand(HostToggleKind kind)
    {
        Command command = new("toggle", $"Toggle {DisplayDescription(kind)}.");

        command.SetAction(async (_, cancellationToken) =>
        {
            await ToggleHostStateAsync(kind, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Helpers
    // ========================================================================

    internal static async Task<ForwardingClientSession?> TryConnectAsync(
        ForwardingRouteKind? route,
        CancellationToken cancellationToken)
    {
        ForwardingClient client = new();
        try
        {
            return route.HasValue
                ? await client.EnableAsync(route.Value, cancellationToken).ConfigureAwait(false)
                : await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            string message = route.HasValue
                ? $"client route={ForwardingServer.GetRouteId(route.Value)}: host not running"
                : "client: host not running";
            await Console.Error.WriteLineAsync(message).ConfigureAwait(false);
            Environment.ExitCode = 1;
            return null;
        }
        catch (IOException exception)
        {
            await Console.Error.WriteLineAsync($"client: unavailable ({exception.Message})").ConfigureAwait(false);
            Environment.ExitCode = 1;
            return null;
        }
    }

    private static async Task SetHostStateAsync(
        HostToggleKind kind,
        bool enabled,
        CancellationToken cancellationToken)
    {
        ForwardingClient client = new();
        try
        {
            await SetHostStateAsync(client, kind, enabled, cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"{DisplayStatusKey(kind)}={FormatBool(enabled)}").ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await Console.Error.WriteLineAsync($"client {DisplayCommandName(kind)}: host not running").ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
        catch (IOException exception)
        {
            await Console.Error.WriteLineAsync(
                $"client {DisplayCommandName(kind)}: unavailable ({exception.Message})")
                .ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
    }

    private static async Task ToggleHostStateAsync(
        HostToggleKind kind,
        CancellationToken cancellationToken)
    {
        ForwardingClient client = new();
        try
        {
            bool enabled = await ToggleHostStateAsync(client, kind, cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"{DisplayStatusKey(kind)}={FormatBool(enabled)}").ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await Console.Error.WriteLineAsync($"client {DisplayCommandName(kind)}: host not running").ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
        catch (IOException exception)
        {
            await Console.Error.WriteLineAsync(
                $"client {DisplayCommandName(kind)}: unavailable ({exception.Message})")
                .ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
    }

    private static Task SetHostStateAsync(
        ForwardingClient client,
        HostToggleKind kind,
        bool enabled,
        CancellationToken cancellationToken)
    {
        return kind switch
        {
            HostToggleKind.Emulation => client.SetEmulationEnabledAsync(enabled, cancellationToken),
            HostToggleKind.PhysicalMotion => client.SetPhysicalMotionEnabledAsync(enabled, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static Task<bool> ToggleHostStateAsync(
        ForwardingClient client,
        HostToggleKind kind,
        CancellationToken cancellationToken)
    {
        return kind switch
        {
            HostToggleKind.Emulation => client.ToggleEmulationEnabledAsync(cancellationToken),
            HostToggleKind.PhysicalMotion => client.TogglePhysicalMotionEnabledAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string DisplayCommandName(HostToggleKind kind)
    {
        return kind switch
        {
            HostToggleKind.Emulation => "emulation",
            HostToggleKind.PhysicalMotion => "physical-motion",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string DisplayDescription(HostToggleKind kind)
    {
        return kind switch
        {
            HostToggleKind.Emulation => "global emulation forwarding",
            HostToggleKind.PhysicalMotion => "global physical motion forwarding",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string DisplayStatusKey(HostToggleKind kind)
    {
        return kind switch
        {
            HostToggleKind.Emulation => "emulationEnabled",
            HostToggleKind.PhysicalMotion => "physicalMotionEnabled",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private enum HostToggleKind
    {
        Emulation,
        PhysicalMotion,
    }
}
