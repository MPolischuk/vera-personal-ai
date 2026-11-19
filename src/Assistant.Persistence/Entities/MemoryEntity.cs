using Assistant.Core.Memory;
using NpgsqlTypes;
using Pgvector;

namespace Assistant.Persistence.Entities;

internal sealed class MemoryEntity : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public MemoryKind Kind { get; set; }
    public string? Content { get; set; }
    public Guid? SourceConversationId { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public Vector? Embedding { get; set; }
    public string EmbeddingModel { get; set; } = string.Empty;
    public WriteProvenance WriteProvenance { get; set; }
    public bool Pinned { get; set; }
    public string? IndexTerms { get; set; }

    /// <summary>Evidence memory ids for an inferred row. Null when the row cites none.</summary>
    public List<Guid>? DerivedFrom { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Short tombstone reason: <c>user</c> or <c>inferred</c>. Null while the row is current.</summary>
    public string? DeletionReason { get; set; }

    /// <summary>
    /// Inclusive start, exclusive end. An open row runs to infinity.
    /// A replacement closes the end and leaves the text unchanged.
    /// </summary>
    public NpgsqlRange<DateTime> ValidPeriod { get; set; }

    /// <summary>Id of the row that replaced this one. Null while the row is current.</summary>
    public Guid? SupersededBy { get; set; }

    public UserEntity User { get; set; } = null!;
}
