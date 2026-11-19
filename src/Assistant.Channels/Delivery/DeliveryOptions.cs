using Microsoft.Extensions.Options;

namespace Assistant.Channels.Delivery;

/// <summary><c>Delivery</c> section. Templates are intentionally unread (stage decision S4).</summary>
public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    public const string FirstSuccess = "FirstSuccess";

    public const string FanOut = "FanOut";

    public string[] Order { get; set; } = ["hub", "webpush", "telegram", "whatsapp"];

    public string Mode { get; set; } = FirstSuccess;

    public TimeSpan HubAckTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public int MaxNotificationsPerHour { get; set; } = 30;

    public QuietHoursOptions QuietHours { get; set; } = new();
}

/// <summary>Local quiet-hours window. Default 23:30–08:00.</summary>
public sealed class QuietHoursOptions
{
    public bool Enabled { get; set; } = true;

    public string Start { get; set; } = "23:30";

    public string End { get; set; } = "08:00";
}

/// <summary>Validates <see cref="DeliveryOptions"/> before the host accepts traffic.</summary>
public sealed class DeliveryOptionsValidator : IValidateOptions<DeliveryOptions>
{
    public ValidateOptionsResult Validate(string? name, DeliveryOptions options)
    {
        if (options.Order is null || options.Order.Length == 0 || options.Order.Any(string.IsNullOrWhiteSpace))
        {
            return ValidateOptionsResult.Fail("Delivery:Order must list at least one channel.");
        }

        if (options.Mode is not (DeliveryOptions.FirstSuccess or DeliveryOptions.FanOut))
        {
            return ValidateOptionsResult.Fail("Delivery:Mode must be FirstSuccess or FanOut.");
        }

        if (options.HubAckTimeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("Delivery:HubAckTimeout must be positive.");
        }

        if (options.MaxNotificationsPerHour <= 0)
        {
            return ValidateOptionsResult.Fail("Delivery:MaxNotificationsPerHour must be positive.");
        }

        if (options.QuietHours is null)
        {
            return ValidateOptionsResult.Fail("Delivery:QuietHours is required.");
        }

        if (!QuietHoursPolicy.TryParseTime(options.QuietHours.Start, out _))
        {
            return ValidateOptionsResult.Fail("Delivery:QuietHours:Start must be HH:mm.");
        }

        if (!QuietHoursPolicy.TryParseTime(options.QuietHours.End, out _))
        {
            return ValidateOptionsResult.Fail("Delivery:QuietHours:End must be HH:mm.");
        }

        return ValidateOptionsResult.Success;
    }
}
