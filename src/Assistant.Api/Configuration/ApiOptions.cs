namespace Assistant.Api.Configuration;

internal sealed class ApiOptions
{
    public const string SectionName = "Assistant:Api";

    public IList<string> AllowedOrigins { get; } = [];

    public bool VerifySchemaOnStart { get; set; } = true;
}
