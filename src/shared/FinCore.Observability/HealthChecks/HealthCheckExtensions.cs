using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FinCore.Observability.HealthChecks;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddFinCoreHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks();
        return services;
    }
}
