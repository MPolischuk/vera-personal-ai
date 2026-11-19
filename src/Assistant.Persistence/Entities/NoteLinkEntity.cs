namespace Assistant.Persistence.Entities;

internal sealed class NoteLinkEntity
{
    public Guid NoteId { get; set; }

    public Guid OtherNoteId { get; set; }

    public NoteEntity Note { get; set; } = null!;

    public NoteEntity Other { get; set; } = null!;
}
