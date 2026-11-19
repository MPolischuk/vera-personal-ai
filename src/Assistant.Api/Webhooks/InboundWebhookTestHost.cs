using System.Text;

namespace Assistant.Api.Webhooks;

/// <summary>Counts calls to the test endpoint. Production does not map that endpoint.</summary>
internal sealed class InboundWebhookTestProbe
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public string? LastBody { get; private set; }

    public void Hit(string body)
    {
        Interlocked.Increment(ref _calls);
        LastBody = body;
    }
}

/// <summary>
/// Maps <c>/api/hooks/{source}</c> only when <c>Webhooks:Inbound:MountTestEndpoint</c> is true.
/// The production host leaves the flag unset.
/// </summary>
internal static class InboundWebhookTestHost
{
    public const string ConfigurationKey = "Webhooks:Inbound:MountTestEndpoint";

    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Configuration.GetValue<bool>(ConfigurationKey))
        {
            return;
        }

        app.MapPost("/api/hooks/{source}", async (HttpContext context, InboundWebhookTestProbe probe) =>
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            probe.Hit(body);
            return Results.Text("ok");
        }).AllowAnonymous().RequireVerifiedInboundWebhook();

        app.MapPost("/api/webhook-filter-probe", () => Results.Text("ok"))
            .AllowAnonymous()
            .RequireVerifiedInboundWebhook();
    }
}
