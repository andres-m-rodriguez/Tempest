using System.Windows.Input;

namespace Tempest;

/// <summary>Base of every generated command state. Implements <see cref="ICommand"/> —
/// the XAML family's command protocol — so a state binds directly to Button.Command
/// and friends: Execute routes through the fire-safe TryExecute, CanExecute gates on
/// <see cref="IsLoading"/>, and CanExecuteChanged fires on both loading edges so
/// bound controls disable themselves while a run is in flight. Blazor never calls the
/// interface; markup there talks to the state directly.</summary>
public abstract class CommandStateBase : ICommand
{
    private readonly ITempestComponent _component;
    private readonly Func<bool>? _canExecute;
    private CancellationTokenSource? _cts;
    private int _version;

    private protected CommandStateBase(ITempestComponent component, Func<bool>? canExecute)
    {
        _component = component;
        _canExecute = canExecute;
        component.RegisterCommand(this);
    }

    /// <summary>Raised when <see cref="IsLoading"/> changes, and on demand via
    /// <see cref="RaiseCanExecuteChanged"/> — bound XAML controls re-query
    /// <see cref="ICommand.CanExecute"/> and update IsEnabled.</summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>True while the command runs; the component re-renders on both edges.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>True when the last <c>TryExecute</c> failed.</summary>
    public bool IsError => Error is not null;

    /// <summary>The exception caught by the last <c>TryExecute</c>, cleared on the next run.</summary>
    public Exception? Error { get; private set; }

    public void ClearError()
    {
        if (Error is null)
            return;
        Error = null;
        _component.Rerender();
    }

    /// <summary>Nudges bound controls to re-query CanExecute — for enablement inputs
    /// the state cannot observe itself.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    bool ICommand.CanExecute(object? parameter)
        => !IsLoading && (_canExecute?.Invoke() ?? true) && CanExecuteCore(parameter);

    void ICommand.Execute(object? parameter) => _ = ExecuteCore(parameter);

    /// <summary>Whether the command accepts this parameter; the loading gate is the
    /// base's job. Parameterless commands accept anything; an event command requires
    /// its record.</summary>
    private protected virtual bool CanExecuteCore(object? parameter) => true;

    /// <summary>The fire-safe run a XAML control invokes — each shape routes it to its
    /// TryExecute, so a button click can never throw.</summary>
    private protected abstract Task ExecuteCore(object? parameter);

    /// <summary>
    /// Runs one command lifecycle with latest-wins semantics: starting a run while another
    /// is in flight cancels the previous run, and a superseded run's outcome — result or
    /// exception — never touches <see cref="IsLoading"/>, <see cref="Error"/> or the commit.
    /// </summary>
    private protected async Task<TResult?> RunAsync<TResult>(
        Func<CancellationToken, Task<TResult>> invoke, bool propagate, Action<TResult>? commit)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var version = ++_version;

        IsLoading = true;
        Error = null;
        RaiseCanExecuteChanged();
        _component.Rerender();

        TResult result;
        try
        {
            result = await invoke(token);
        }
        catch (OperationCanceledException) when (version != _version)
        {
            return default; // superseded and observed its cancellation — discard
        }
        catch (Exception ex)
        {
            if (version != _version)
                return default; // superseded — even a failure is discarded

            IsLoading = false;
            RaiseCanExecuteChanged();
            if (propagate)
            {
                _component.Rerender();
                throw;
            }
            Error = ex;
            _component.Rerender();
            return default;
        }

        if (version != _version)
            return default; // superseded — a stale success must not overwrite the newer run

        commit?.Invoke(result);
        IsLoading = false;
        RaiseCanExecuteChanged();
        _component.Rerender();
        return result;
    }

    private protected Task RunAsync(Func<CancellationToken, Task> invoke, bool propagate)
        => RunAsync<object?>(async ct => { await invoke(ct); return null; }, propagate, null);
}

/// <summary>Generated per [Command] returning Task, ValueTask or void.</summary>
public sealed class CommandState(
    ITempestComponent component,
    Func<CancellationToken, Task> command,
    Func<bool>? canExecute = null) : CommandStateBase(component, canExecute)
{
    private readonly Func<CancellationToken, Task> _command = command;

    /// <summary>Runs the lifecycle; exceptions propagate to the caller.</summary>
    public Task Execute() => RunAsync(_command, propagate: true);

    /// <summary>Never throws — the exception lands in <see cref="CommandStateBase.Error"/>.</summary>
    public Task TryExecute() => RunAsync(_command, propagate: false);

    private protected override Task ExecuteCore(object? parameter) => TryExecute();
}

/// <summary>
/// Generated per [Command] returning Task&lt;T&gt;, ValueTask&lt;T&gt; or a plain value:
/// the last successful value is kept in <see cref="Result"/>, and stays visible while
/// the next run is loading.
/// </summary>
public sealed class CommandState<TResult> : CommandStateBase
{
    private readonly Func<CancellationToken, Task<TResult>> _command;

    public CommandState(
        ITempestComponent component,
        Func<CancellationToken, Task<TResult>> command,
        Func<bool>? canExecute = null)
        : base(component, canExecute) => _command = command;

    /// <summary>The last successful result; default until the first success.</summary>
    public TResult? Result { get; private set; }

    /// <summary>True once a run has succeeded — disambiguates a default-valued Result.</summary>
    public bool HasResult { get; private set; }

    /// <summary>Runs the lifecycle and returns the result; exceptions propagate to the caller.
    /// A superseded run returns default.</summary>
    public Task<TResult?> Execute() => RunAsync(_command, propagate: true, Commit);

    /// <summary>Never throws — the exception lands in <see cref="CommandStateBase.Error"/>
    /// and default is returned.</summary>
    public Task<TResult?> TryExecute() => RunAsync(_command, propagate: false, Commit);

    private void Commit(TResult result)
    {
        Result = result;
        HasResult = true;
    }

    private protected override Task ExecuteCore(object? parameter) => TryExecute();
}

/// <summary>Generated per [Event, Command]: a command that takes its event record.</summary>
public sealed class EventCommandState<TEvent>(
    ITempestComponent component,
    Func<TEvent, CancellationToken, Task> command,
    Func<bool>? canExecute = null) : CommandStateBase(component, canExecute)
{
    /// <summary>Runs the lifecycle; exceptions propagate to the caller.</summary>
    public Task Execute(TEvent arg) => RunAsync(ct => command(arg, ct), propagate: true);

    /// <summary>Never throws — the exception lands in <see cref="CommandStateBase.Error"/>.</summary>
    public Task TryExecute(TEvent arg) => RunAsync(ct => command(arg, ct), propagate: false);

    // Through ICommand, CommandParameter carries the event record: a control stays
    // disabled until its parameter binding resolves to the right type.
    private protected override bool CanExecuteCore(object? parameter) => parameter is TEvent;

    private protected override Task ExecuteCore(object? parameter)
        => parameter is TEvent arg ? TryExecute(arg) : Task.CompletedTask;
}
