namespace Assistant.Api.Webhooks;

/// <summary>
/// Second barrier: an endpoint mapped outside the middleware, or reached without a verified feature, is rejected.
/// </summary>
internal sealed class RequireVerifiedInboundWebhookFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.HttpContext.Features.Get<IVerifiedInboundWebhookFeature>() is null)
        {
            return Results.Text(
                InboundWebhookCodes.SignatureInvalid,
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return await next(context);
    }
}

/// <summary>Marks an endpoint as requiring <see cref="IVerifiedInboundWebhookFeature"/>.</summary>
public static class InboundWebhookEndpointExtensions
{
    public static RouteHandlerBuilder RequireVerifiedInboundWebhook(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter<RequireVerifiedInboundWebhookFilter>();
    }
}
