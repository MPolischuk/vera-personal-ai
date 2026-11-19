namespace Assistant.Channels.Telegram.Api.Dto;

/// <summary>Envelope of every Bot API JSON response.</summary>
public sealed class TelegramResponse<T>
{
    public bool Ok { get; set; }

    public T? Result { get; set; }

    public int? ErrorCode { get; set; }

    public string? Description { get; set; }

    public TelegramResponseParameters? Parameters { get; set; }
}

/// <summary>Optional <c>parameters</c> object. <c>retry_after</c> is seconds.</summary>
public sealed class TelegramResponseParameters
{
    public int? RetryAfter { get; set; }
}
