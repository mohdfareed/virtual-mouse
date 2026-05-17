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
}
