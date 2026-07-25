using Microsoft.Extensions.DependencyInjection;

namespace Tempest;

/// <summary>WinUI app-level Tempest services. XAML instantiates pages and controls
/// through their parameterless constructors, so both the bus and the service provider
/// are ambient: one app-wide <see cref="EventBus"/> by default, replaceable at startup
/// — or per component through <see cref="StatefulControl.Bus"/>.</summary>
public static class TempestWinUI
{
    public static IEventBus Bus { get; set; } = new EventBus();

    /// <summary>The app's service provider, exposed to StatefulPage and
    /// StatefulControl as their <c>Services</c> member and to generated constructor
    /// bridges — one ambient, stored on <see cref="TempestServices"/> so host-neutral
    /// generated code can reach it. Set directly, or through
    /// <see cref="UseServices"/>.</summary>
    public static IServiceProvider? Services
    {
        get => TempestServices.Provider;
        set => TempestServices.Provider = value;
    }

    /// <summary>Wires Tempest to the app's container: exposes it to pages and
    /// controls, and adopts the container's bus when one is registered.</summary>
    public static void UseServices(IServiceProvider services)
    {
        Services = services;
        if (services.GetService<IEventBus>() is { } bus)
            Bus = bus;
    }
}
