using System.Windows.Input;
using Tempest;
using Xunit;

namespace Tempest.Abstract.Tests;

public class CommandStateICommandTests
{
    private sealed class FakeComponent : ITempestComponent
    {
        public int Rerenders { get; private set; }

        public void Rerender() => Rerenders++;

        public void DispatchReaction(Func<Task> reaction) => _ = reaction();
    }

    [Fact]
    public async Task ExecuteViaICommandRunsFireSafe()
    {
        var state = new CommandState(new FakeComponent(), _ => throw new InvalidOperationException("boom"));
        ICommand command = state;

        command.Execute(null);   // must not throw — routed through TryExecute
        await Task.Yield();

        Assert.True(state.IsError);
        Assert.Equal("boom", state.Error!.Message);
    }

    [Fact]
    public async Task CanExecuteGatesOnLoadingAndRaisesOnBothEdges()
    {
        var gate = new TaskCompletionSource();
        var state = new CommandState(new FakeComponent(), _ => gate.Task);
        ICommand command = state;
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        Assert.True(command.CanExecute(null));

        var run = state.Execute();
        Assert.False(command.CanExecute(null));   // loading
        Assert.Equal(1, raised);

        gate.SetResult();
        await run;

        Assert.True(command.CanExecute(null));
        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task ResultBearingStateWorksThroughICommand()
    {
        var state = new CommandState<int>(new FakeComponent(), _ => Task.FromResult(42));

        ((ICommand)state).Execute(null);
        await Task.Yield();

        Assert.True(state.HasResult);
        Assert.Equal(42, state.Result);
    }

    [Fact]
    public async Task EventCommandTakesItsRecordAsCommandParameter()
    {
        string? seen = null;
        var state = new EventCommandState<string>(new FakeComponent(), (e, _) =>
        {
            seen = e;
            return Task.CompletedTask;
        });
        ICommand command = state;

        Assert.False(command.CanExecute(null));      // no parameter yet — stays disabled
        Assert.False(command.CanExecute(123));       // wrong type
        Assert.True(command.CanExecute("payload"));

        command.Execute(123);                        // wrong type — ignored, no run
        Assert.Null(seen);

        command.Execute("payload");
        await Task.Yield();
        Assert.Equal("payload", seen);
    }

    [Fact]
    public async Task PredicateGatesCanExecute()
    {
        var allowed = false;
        var state = new CommandState(new FakeComponent(), _ => Task.CompletedTask, () => allowed);
        ICommand command = state;

        Assert.False(command.CanExecute(null));
        allowed = true;
        Assert.True(command.CanExecute(null));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RaiseCanExecuteChangedNudgesSubscribers()
    {
        var state = new CommandState(new FakeComponent(), _ => Task.CompletedTask);
        var raised = 0;
        ((ICommand)state).CanExecuteChanged += (_, _) => raised++;

        state.RaiseCanExecuteChanged();

        Assert.Equal(1, raised);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task StoreMutateBatchesAndBroadcastsOnce()
    {
        var store = new ProbeStore(new EventBus());
        var mutated = 0;

        await store.Do(() => mutated++);

        Assert.Equal(1, mutated);
        Assert.Equal(1, store.Broadcasts);
    }

    private sealed class ProbeStore(IEventBus bus) : StatefulStore(bus)
    {
        public int Broadcasts { get; private set; }

        public ProbeStore Self => this;

        public Task Do(Action mutation) => Mutate(mutation);

        protected override void RegisterTempestHandlers(IEventBus bus)
            => PropertyChanged += (_, _) => Broadcasts++;
    }

    [Fact]
    public async Task LatestWinsSurvivesICommandEntry()
    {
        var first = new TaskCompletionSource<int>();
        var calls = 0;
        var state = new CommandState<int>(new FakeComponent(), _ =>
            ++calls == 1 ? first.Task : Task.FromResult(2));
        ICommand command = state;

        command.Execute(null);          // run 1, parked on the gate
        command.Execute(null);          // run 2, supersedes and completes
        await Task.Yield();

        first.SetResult(1);             // stale success must not overwrite
        await Task.Yield();

        Assert.Equal(2, state.Result);
        Assert.False(state.IsLoading);
    }
}
