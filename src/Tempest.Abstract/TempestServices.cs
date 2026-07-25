using Microsoft.Extensions.DependencyInjection;

namespace Tempest;

/// <summary>The ambient service provider generated constructor bridges resolve from.
/// XAML activation (Frame.Navigate, markup) uses parameterless constructors, so a
/// component declaring a primary constructor gets a generated parameterless bridge
/// that resolves each parameter here and chains. Set once at startup — host packages
/// expose a UseServices helper that also wires the bus.</summary>
public static class TempestServices
{
    public static IServiceProvider? Provider { get; set; }

    /// <summary>Called by generated constructor bridges — not intended for user code,
    /// which should resolve through its own constructor parameters instead.</summary>
    public static T Resolve<T>() where T : notnull
        => (Provider ?? throw new InvalidOperationException(
                $"Cannot construct a component that injects {typeof(T).Name}: no service provider is configured. Call UseServices(provider) on your host (TempestWinUI, TempestWpf) at startup."))
            .GetRequiredService<T>();
}
