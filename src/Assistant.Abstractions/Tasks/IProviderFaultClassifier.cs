namespace Assistant.Abstractions.Tasks;

/// <summary>How a provider failure should be recorded on a task.</summary>
public enum ProviderFaultClass
{
    /// <summary>Anything that is not a budget cap or a transport failure.</summary>
    Other = 0,

    /// <summary>HTTP 403 from the provider. Not retried.</summary>
    Budget = 1,

    /// <summary>Transport failure with no HTTP status.</summary>
    Network = 2,
}

/// <summary>
/// Classifies a provider exception by status, never by the message text.
/// </summary>
public interface IProviderFaultClassifier
{
    /// <summary>Classifies <paramref name="exception"/> and its inner exceptions.</summary>
    public ProviderFaultClass Classify(Exception exception);
}
