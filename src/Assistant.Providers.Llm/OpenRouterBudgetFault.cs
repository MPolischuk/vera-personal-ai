using System.ClientModel;
using System.Net.Sockets;
using Assistant.Abstractions.Tasks;

namespace Assistant.Providers.Llm;

/// <summary>
/// 403 is a budget cap. A transport exception with no HTTP status is a network failure.
/// The message text is never consulted.
/// </summary>
public sealed class OpenRouterBudgetFault : IProviderFaultClassifier
{
    public ProviderFaultClass Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        int? status = null;
        var transport = false;
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ClientResultException client)
            {
                if (client.Status == 403)
                {
                    return ProviderFaultClass.Budget;
                }

                if (client.Status != 0)
                {
                    status = client.Status;
                }
            }

            if (current is HttpRequestException or IOException or SocketException)
            {
                transport = true;
            }
        }

        return status is null && transport
            ? ProviderFaultClass.Network
            : ProviderFaultClass.Other;
    }
}
