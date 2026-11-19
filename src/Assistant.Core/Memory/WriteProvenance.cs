namespace Assistant.Core.Memory;

/// <summary>
/// Origin of the current durable write (ADR-0018). Stored in snake_case.
/// </summary>
public enum WriteProvenance
{
    User,
    ModelClean,
    ModelContaminatedConfirmed,
    Inferred,
}

public static class WriteProvenanceExtensions
{
    extension(WriteProvenance)
    {
        public static WriteProvenance FromModelTurn(bool turnContaminated)
            => turnContaminated
                ? WriteProvenance.ModelContaminatedConfirmed
                : WriteProvenance.ModelClean;
    }
}
