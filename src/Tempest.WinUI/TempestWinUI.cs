namespace Tempest;

/// <summary>WinUI app-level Tempest services. XAML instantiates controls through their
/// parameterless constructors, so the bus is ambient: one app-wide
/// <see cref="EventBus"/> by default, replaceable at startup — or per control through
/// <see cref="StatefulControl.Bus"/>.</summary>
public static class TempestWinUI
{
    public static IEventBus Bus { get; set; } = new EventBus();
}
