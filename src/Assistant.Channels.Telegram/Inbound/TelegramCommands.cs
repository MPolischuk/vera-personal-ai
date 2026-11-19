using Assistant.Channels.Telegram.Api.Dto;

namespace Assistant.Channels.Telegram.Inbound;

/// <summary>Channel commands resolved without the model, plus the menu published with <c>setMyCommands</c>.</summary>
internal static class TelegramCommands
{
    public const string Start = "start";

    public const string Nueva = "nueva";

    public const string Recordatorios = "recordatorios";

    public const string Ayuda = "ayuda";

    public static readonly IReadOnlyList<BotCommand> Menu =
    [
        new() { Command = Start, Description = "Empezar a hablar con Vera" },
        new() { Command = Nueva, Description = "Empezar otra conversación" },
        new() { Command = Recordatorios, Description = "Ver los recordatorios activos" },
        new() { Command = Ayuda, Description = "Qué puedo hacer por acá" },
    ];

    public static bool TryParse(string? text, out string name)
    {
        name = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed[0] != '/')
        {
            return false;
        }

        var end = 1;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]) && trimmed[end] != '@')
        {
            end++;
        }

        if (end == 1)
        {
            return false;
        }

        name = trimmed[1..end].ToLowerInvariant();
        return name is Start or Nueva or Recordatorios or Ayuda;
    }
}
