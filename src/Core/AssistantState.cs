namespace NEO.Core;

public enum AssistantState { Idle, Connecting, Ready, Listening, Processing, Speaking, Interrupted, Error, Disconnected }

public sealed class AssistantStateMachine
{
    public AssistantState State { get; private set; } = AssistantState.Idle;
    public event Action<AssistantState>? Changed;

    public bool TryTransition(AssistantState next)
    {
        if (next == State) return true;
        bool allowed = next is AssistantState.Error or AssistantState.Disconnected or AssistantState.Connecting
            || (State, next) switch
            {
                (AssistantState.Idle, AssistantState.Ready) => true,
                (AssistantState.Connecting, AssistantState.Ready) => true,
                (AssistantState.Ready, AssistantState.Listening or AssistantState.Processing or AssistantState.Idle) => true,
                (AssistantState.Listening, AssistantState.Processing or AssistantState.Interrupted or AssistantState.Ready) => true,
                (AssistantState.Processing, AssistantState.Speaking or AssistantState.Ready or AssistantState.Interrupted) => true,
                (AssistantState.Speaking, AssistantState.Ready or AssistantState.Interrupted) => true,
                (AssistantState.Interrupted, AssistantState.Ready or AssistantState.Listening) => true,
                (AssistantState.Error, AssistantState.Connecting or AssistantState.Ready or AssistantState.Processing) => true,
                _ => false
            };
        if (!allowed) return false;
        State = next;
        Changed?.Invoke(next);
        return true;
    }
}
