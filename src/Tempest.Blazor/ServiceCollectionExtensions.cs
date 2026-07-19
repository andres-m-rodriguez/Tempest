using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tempest;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTempest(this IServiceCollection services)
    {
        services.AddScoped<IEventBus>(sp =>
            new EventBus(sp.GetService<ILoggerFactory>()?.CreateLogger<EventBus>()));
        return services;
    }
}
