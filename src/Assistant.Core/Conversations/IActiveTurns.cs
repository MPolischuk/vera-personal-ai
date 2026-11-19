namespace Assistant.Core.Conversations;

public interface IActiveTurns
{
    /// <summary>False if there was no turn in progress for that conversation.</summary>
    public bool TryInterrupt(Guid conversationId);

    /// <summary>True while a turn for that conversation is still registered.</summary>
    public bool IsInProgress(Guid conversationId);
}
