using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualMouse.Hosting;

namespace Refactor.Cli;

internal static class Commands
{
    // MARK: Commands
    // ========================================================================

    public static Command CreateServerCommand()
    {
        Command server = new("server");
        Command run = new("run", "Run the server.");
        run.SetAction(RunServerAsync);
        Command status = new("status", "Print server status.");
        status.SetAction(RunServerStatusAsync);
        server.Subcommands.Add(run);
        server.Subcommands.Add(status);
        return server;
    }

    public static Command CreateClientCommand()
    {
        Command client = new("client");
        Command run = new("run", "Connect to the server.");
        run.SetAction(RunClientAsync);
        client.Subcommands.Add(run);
        return client;
    }

    // MARK: Handlers
    // ========================================================================

    private static async Task RunServerAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        _ = parseResult;
        using IHost app = AppSetup.Create();
        await app.Services.GetRequiredService<VirtualMouseServer>().RunAsync(cancellationToken);
    }

    private static async Task RunServerStatusAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        _ = parseResult;
        using IHost app = AppSetup.Create();
        await using VirtualMouseClient client = app.Services.GetRequiredService<VirtualMouseClient>();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            await client.ConnectAsync(timeout.Token);
            ServerStatus status = await client.GetStatusAsync(timeout.Token);
            await Console.Out.WriteLineAsync(
                    $"server running=true connectedClients={status.ConnectedClientCount}")
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await Console.Out.WriteLineAsync("server running=false").ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            await Console.Error.WriteLineAsync($"server status: unavailable ({exception.Message})")
                .ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
    }

    private static async Task RunClientAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        _ = parseResult;
        using IHost app = AppSetup.Create();
        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("client");
        await using VirtualMouseClient client = app.Services.GetRequiredService<VirtualMouseClient>();
        client.ConnectionChanged += (_, update) =>
        {
            logger.LogInformation(
                "Connection changed: {State} client={ClientId}",
                update.State,
                update.ClientId?.ToString() ?? "none");
        };

        await client.ConnectAsync(cancellationToken);
        await client.WaitAsync(cancellationToken);
    }
}
