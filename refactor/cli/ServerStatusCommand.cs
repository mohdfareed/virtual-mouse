using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VirtualMouse.Forwarding;
using VirtualMouse.Hosting;
using VirtualMouse.Runtime;

namespace Refactor.Cli;

internal static class ServerStatusCommand
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static Command Create()
    {
        Command status = new("status", "Print server status.");
        status.Options.Add(new Option<bool>("--json")
        {
            Description = "Print status as JSON.",
        });
        status.SetAction(RunAsync);
        return status;
    }

    private static async Task RunAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        bool json = parseResult.GetValue<bool>("--json");
        using IHost app = AppSetup.Create();
        await using VirtualMouseClient client = app.Services.GetRequiredService<VirtualMouseClient>();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            await client.ConnectAsync(timeout.Token);
            ServerStatus status = await client.GetStatusAsync(timeout.Token);
            if (json)
            {
                await PrintJsonAsync(status).ConfigureAwait(false);
            }
            else
            {
                await PrintAsync(status).ConfigureAwait(false);
            }
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

    private static async Task PrintAsync(ServerStatus status)
    {
        await WriteSectionAsync("Server").ConfigureAwait(false);
        await WriteItemAsync("running", "true").ConfigureAwait(false);
        await WriteItemAsync("connectedClients", status.ConnectedClientCount).ConfigureAwait(false);

        await WriteSectionAsync("Runtime").ConfigureAwait(false);
        await WriteItemAsync("activeClient", status.Runtime.ActiveClientId?.ToString() ?? "none").ConfigureAwait(false);
        await WriteItemAsync("foregroundPid", status.Runtime.ForegroundProcessId).ConfigureAwait(false);

        await WriteSectionAsync("Clients").ConfigureAwait(false);
        if (status.Runtime.Clients.Count == 0)
        {
            await WriteIndentedAsync("none").ConfigureAwait(false);
        }

        foreach (ClientStatus client in status.Runtime.Clients)
        {
            await WriteIndentedAsync(
                    $"{client.ClientId} profile={client.ProfileId} active={client.IsActive} process={client.ClientProcessId} steamAppId={FormatAppId(client.SteamAppId)}")
                .ConfigureAwait(false);
            await WriteIndentedAsync($"receivers: {FormatList(client.ReceiverProcesses)}", 4).ConfigureAwait(false);
            await WriteIndentedAsync($"observed:  {FormatProcesses(client.ObservedProcesses)}", 4).ConfigureAwait(false);
            await WriteIndentedAsync($"owned:     {FormatProcesses(client.OwnedProcesses)}").ConfigureAwait(false);
            await WriteIndentedAsync($"blocked:   {FormatProcesses(client.BlockedProcesses)}").ConfigureAwait(false);
        }

        await WriteSectionAsync("Inputs").ConfigureAwait(false);
        await WriteIndentedAsync(
                $"physicalSdl running={status.Inputs.PhysicalControllers.Running} controllers={status.Inputs.PhysicalControllers.ControllerCount} error={status.Inputs.PhysicalControllers.LastError ?? "none"}")
            .ConfigureAwait(false);
        foreach (string controllerId in status.Inputs.PhysicalControllers.ControllerIds)
        {
            await WriteIndentedAsync($"physical {controllerId}", 4).ConfigureAwait(false);
        }

        await WriteIndentedAsync(
                $"rawInput running={status.Inputs.Mouse.Running} connected={status.Inputs.Mouse.SourceConnected} error={status.Inputs.Mouse.LastError ?? "none"}")
            .ConfigureAwait(false);

        await WriteSectionAsync("Controller Output").ConfigureAwait(false);
        await WriteItemAsync("enabled", status.Forwarding.ControllerOutputEnabled).ConfigureAwait(false);
        await WriteItemAsync("physicalMotion", status.Forwarding.PhysicalMotionEnabled).ConfigureAwait(false);
        await WriteItemAsync("slots", status.Forwarding.Slots.Count).ConfigureAwait(false);
        if (status.Forwarding.Slots.Count == 0)
        {
            await WriteIndentedAsync("none").ConfigureAwait(false);
        }

        foreach (ControllerSlotStatus slot in status.Forwarding.Slots)
        {
            await WriteIndentedAsync(
                    $"{slot.ControllerId} output={slot.Output} connected={slot.OutputConnected} steam={slot.SteamEndpointCount} activeSteam={slot.HasActiveSteamEndpoint} physical={slot.HasPhysicalEndpoint}")
                .ConfigureAwait(false);
            await WriteIndentedAsync(
                    $"features physical={slot.PhysicalFeatures?.ToString() ?? "none"} activeSteam={slot.ActiveSteamFeatures?.ToString() ?? "none"}",
                    4)
                .ConfigureAwait(false);
        }

        await WriteSectionAsync("Controller Pipes").ConfigureAwait(false);
        await WriteItemAsync("count", status.ControllerPipes.Count).ConfigureAwait(false);
        if (status.ControllerPipes.Count == 0)
        {
            await WriteIndentedAsync("none").ConfigureAwait(false);
        }

        foreach (ControllerPipeStatus pipe in status.ControllerPipes)
        {
            await WriteIndentedAsync(
                    $"{pipe.ClientId} connected={pipe.Connected} controllers={pipe.Controllers.Count}")
                .ConfigureAwait(false);
            foreach (ClientControllerStatus controller in pipe.Controllers)
            {
                await WriteIndentedAsync(
                        $"controller index={controller.ControllerIndex} physical={controller.PhysicalControllerId} features={controller.Features}",
                        4)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task PrintJsonAsync(ServerStatus status)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(status, JsonOptions)).ConfigureAwait(false);
    }

    private static async Task WriteSectionAsync(string title)
    {
        await Console.Out.WriteLineAsync().ConfigureAwait(false);
        await Console.Out.WriteLineAsync(title).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(new string('-', title.Length)).ConfigureAwait(false);
    }

    private static Task WriteItemAsync(string name, object value)
    {
        return WriteIndentedAsync($"{name,-18} {value}");
    }

    private static Task WriteIndentedAsync(string value, int spaces = 2)
    {
        return Console.Out.WriteLineAsync(new string(' ', spaces) + value);
    }

    private static string FormatList(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? "none" : string.Join(", ", values);
    }

    private static string FormatAppId(uint? appId)
    {
        return appId.HasValue ? appId.Value.ToString(CultureInfo.InvariantCulture) : "none";
    }

    private static string FormatProcesses(IReadOnlyList<ObservedGameProcess> processes)
    {
        return processes.Count == 0
            ? "none"
            : string.Join(", ", processes.Select(process => $"{process.ProcessName}:{process.ProcessId}"));
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
