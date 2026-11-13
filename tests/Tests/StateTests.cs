using NEO.Core;

namespace NEO.Tests;

public sealed class StateTests
{
    [Fact]
    public void RequiresReadyBeforeListeningAndSupportsInterruption()
    {
        var machine = new AssistantStateMachine();
        Assert.False(machine.TryTransition(AssistantState.Listening));
        Assert.True(machine.TryTransition(AssistantState.Connecting));
        Assert.True(machine.TryTransition(AssistantState.Ready));
        Assert.True(machine.TryTransition(AssistantState.Listening));
        Assert.True(machine.TryTransition(AssistantState.Processing));
        Assert.True(machine.TryTransition(AssistantState.Speaking));
        Assert.True(machine.TryTransition(AssistantState.Interrupted));
        Assert.True(machine.TryTransition(AssistantState.Ready));
    }

    [Fact]
    public void DisconnectRequiresReconnectAndDoesNotEmitDuplicateEvents()
    {
        var machine = new AssistantStateMachine();
        int events = 0; machine.Changed += _ => events++;
        Assert.True(machine.TryTransition(AssistantState.Disconnected));
        Assert.True(machine.TryTransition(AssistantState.Disconnected));
        Assert.False(machine.TryTransition(AssistantState.Ready));
        Assert.Equal(1, events);
        Assert.True(machine.TryTransition(AssistantState.Connecting));
        Assert.True(machine.TryTransition(AssistantState.Ready));
    }
}
