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
}
