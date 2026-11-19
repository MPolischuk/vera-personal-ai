namespace Assistant.Persistence.Entities;

internal sealed class NoteSourceEntity
{
    public Guid Id { get; set; }

    public Guid NoteId { get; set; }

    public int Ordinal { get; set; }

    public string ClaimText { get; set; } = "";

    public string Fragment { get; set; } = "";

    public DateTimeOffset RetrievedAt { get; set; }

    public string? Url { get; set; }

    public Guid? DocumentId { get; set; }

    public int? Page { get; set; }

    public double? BboxLeft { get; set; }

    public double? BboxTop { get; set; }

    public double? BboxRight { get; set; }

    public double? BboxBottom { get; set; }

    public NoteEntity Note { get; set; } = null!;
}
