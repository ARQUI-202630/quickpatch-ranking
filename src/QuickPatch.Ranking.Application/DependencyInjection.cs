using Microsoft.Extensions.DependencyInjection;

using QuickPatch.Ranking.Application.Reputation;

namespace QuickPatch.Ranking.Application;

/// <summary>
/// Capa de aplicación: casos de uso, comandos y consultas (SDD 6.3).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ServiceRequestRatedHandler>();
        return services;
    }
}