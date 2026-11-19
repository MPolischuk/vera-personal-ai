using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Assistant.Providers.Llm.Configuration;

public sealed class ModelsOptions
{
    public const string SectionName = "Assistant:Models";

    [Required]
    public ModelProfileOptions Conversation { get; set; } = new();

    /// <summary>If missing, TaskKind.Utility uses the conversation profile (D5).</summary>
    public ModelProfileOptions? Utility { get; set; }

    /// <summary>If missing, falls back to Utility and then Conversation.</summary>
    public ModelProfileOptions? Summarization { get; set; }

    /// <summary>
    /// Embedding profile. Not a chat <see cref="TaskKind"/> and not part of
    /// <see cref="DeclaredProfiles"/> (PT-36 D6).
    /// </summary>
    [Required]
    [ValidateObjectMembers]
    public EmbeddingProfileOptions Embedding { get; set; } = new();

    internal IEnumerable<ModelProfileOptions> DeclaredProfiles()
    {
        yield return Conversation;
        if (Utility is not null)
        {
            yield return Utility;
        }

        if (Summarization is not null)
        {
            yield return Summarization;
        }
    }
}

public sealed class EmbeddingProfileOptions
{
    [Required]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Remote endpoint. Required for every provider except <c>Local</c>, which embeds
    /// in-process and leaves this empty.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Never in appsettings for a remote provider. User Secrets in development,
    /// environment variable in production. Empty when <see cref="Provider"/> is <c>Local</c>.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public int Dimensions { get; set; }

    /// <summary>
    /// Directory of the fp32 ONNX export when <see cref="Provider"/> is <c>Local</c>.
    /// It must contain <c>model.onnx</c> and <c>model.onnx_data</c>. Weights are not committed.
    /// </summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Prepended to query text before embedding. Empty when the provider has no
    /// instruction prefix (OpenAI).
    /// </summary>
    public string QueryPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Prepended to document text before embedding. Empty when the provider has no
    /// instruction prefix (OpenAI).
    /// </summary>
    public string DocumentPrefix { get; set; } = string.Empty;

    /// <summary>
    /// OpenRouter extras for this profile. Ignored unless <see cref="Provider"/> is openrouter.
    /// </summary>
    public OpenRouterProviderOptions? OpenRouter { get; set; }
}

public sealed class ModelProfileOptions
{
    /// <summary>
    /// Free-form name: stored in usage_records.provider and what distinguishes
    /// OpenRouter from OpenAI, which for MEAI are the same adapter (D2).
    /// </summary>
    [Required]
    public string Provider { get; set; } = string.Empty;

    [Required]
    [Url]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string Model { get; set; } = string.Empty;

    /// <summary>Never in appsettings. User Secrets in development, environment variable in production.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Range(0.0, 2.0)]
    public float? Temperature { get; set; }

    [Range(1, 200_000)]
    public int? MaxOutputTokens { get; set; }

    /// <summary>
    /// OpenRouter-only extras serialized into ChatOptions.AdditionalProperties["provider"].
    /// Ignored when <see cref="Provider"/> is not openrouter. If <see cref="OpenRouterProviderOptions.Sort"/> is null or blank, the provider object omits <c>sort</c>.
    /// </summary>
    public OpenRouterProviderOptions? OpenRouter { get; set; }
}

/// <summary>Optional OpenRouter provider routing prefs (PT-20 D6).</summary>
public sealed class OpenRouterProviderOptions
{
    /// <summary>Sort strategy: latency, price, or throughput. Omitted from the provider object when null or blank.</summary>
    public string? Sort { get; set; }

    /// <summary>Allowlist of provider slugs (e.g. Groq, DeepInfra).</summary>
    public List<string>? Only { get; set; }

    /// <summary>Provider slugs to exclude.</summary>
    public List<string>? Ignore { get; set; }

    /// <summary>Whether OpenRouter may fall back when the preferred providers fail.</summary>
    public bool? AllowFallbacks { get; set; }

    /// <summary>
    /// When true, the provider object includes <c>zdr: true</c> (zero data retention).
    /// Omitted when null or false.
    /// </summary>
    public bool? Zdr { get; set; }

    /// <summary>
    /// Sent as <c>data_collection</c> when it has text. <c>deny</c> is the value this
    /// profile uses. Omitted when null or whitespace.
    /// </summary>
    public string? DataCollection { get; set; }
}

public sealed class CostOptions
{
    public const string SectionName = "Assistant:Costs";

    // Declared and not applied in stage 1a (D9).
    public decimal MonthlyBudgetUsd { get; set; }

    [Range(1, 100)]
    public int WarnAtPercent { get; set; } = 80;

    public Dictionary<string, ModelPriceOptions> Prices { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModelPriceOptions
{
    // Named PerMillion because the classic mistake is loading the per-thousand price
    // and being off by a factor of a thousand downward — the direction that does not hurt.
    [Range(0.0, 1_000.0)]
    public decimal InputPerMillionUsd { get; set; }

    [Range(0.0, 1_000.0)]
    public decimal OutputPerMillionUsd { get; set; }

    /// <summary>
    /// Cache-read price per million tokens. Null charges that slice at
    /// <see cref="InputPerMillionUsd"/>. An explicit 0 is free.
    /// </summary>
    [Range(0.0, 1_000.0)]
    public decimal? CachedInputPerMillionUsd { get; set; }

    /// <summary>
    /// Cache-write price per million tokens. Null charges that slice at
    /// <see cref="InputPerMillionUsd"/>. An explicit 0 is free.
    /// </summary>
    [Range(0.0, 1_000.0)]
    public decimal? CacheWritePerMillionUsd { get; set; }
}
