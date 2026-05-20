using Microsoft.Extensions.DependencyInjection;

namespace SteamInputBridge.Hosting.Client;

/// <summary>Dependency injection registration for the app-facing client.</summary>
public static class ClientServices
{
    /// <summary>Adds the local server client.</summary>
    public static IServiceCollection AddApplicationClient(this IServiceCollection services)
    {
        _ = services.AddTransient<ClientService>();
        _ = services.AddTransient<GameClient>();
        return services;
    }
}
