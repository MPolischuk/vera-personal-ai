using Pgvector;

namespace Assistant.Persistence.Entities;

internal sealed class NoteEntity : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    public Vector Embedding { get; set; } = null!;

    public DateTimeOffset? VerifiedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<NoteSourceEntity> Sources { get; set; } = [];

    public List<NoteLinkEntity> OutgoingLinks { get; set; } = [];
}
