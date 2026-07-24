namespace Tempest;

/// <summary>Infrastructure contract state objects use to reach their owning component.
/// Implemented by each UI host's component bases (Tempest.Blazor's StatefulComponent and
/// StatefulLayoutComponent); not intended to be implemented by user code.</summary>
public interface ITempestComponent
{
    /// <summary>Re-render through the component's sync context.</summary>
    void Rerender();

    /// <summary>Run a reactive hook through the sync context; a throwing hook surfaces
    /// to Blazor's error handling like a throwing event handler, never an unobserved task.</summary>
    void DispatchReaction(Func<Task> reaction);

    /// <summary>Called by every command state on construction. Hosts whose bindings go
    /// through ICommand (XAML controls, stores) keep the registry and raise
    /// CanExecuteChanged on each re-render broadcast, so [CanExecute] members re-gate
    /// buttons when state changes; the default ignores it — Blazor markup re-evaluates
    /// on render without ICommand.</summary>
    void RegisterCommand(CommandStateBase command)
    {
    }
}
