using Assistant.Core.Memory;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class NoteConfiguration : IEntityTypeConfiguration<NoteEntity>
{
    public void Configure(EntityTypeBuilder<NoteEntity> builder)
    {
        builder.ToTable("notes");
        builder.HasKey(note => note.Id);
        builder.Property(note => note.Title).IsRequired();
        builder.Property(note => note.Body).IsRequired();
        builder.Property(note => note.Embedding)
            .HasColumnType($"vector({MemorySchema.EmbeddingDimensions})")
            .IsRequired();

        builder.HasIndex(note => note.UserId)
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(note => note.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops")
            .HasFilter("deleted_at IS NULL");

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(note => note.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(note => note.DeletedAt == null);
    }
}

internal sealed class NoteSourceConfiguration : IEntityTypeConfiguration<NoteSourceEntity>
{
    public void Configure(EntityTypeBuilder<NoteSourceEntity> builder)
    {
        builder.ToTable("note_sources");
        builder.HasKey(source => source.Id);
        builder.Property(source => source.ClaimText).IsRequired();
        builder.Property(source => source.Fragment).IsRequired();
        builder.HasIndex(source => new { source.NoteId, source.Ordinal }).IsUnique();

        builder.HasOne(source => source.Note)
            .WithMany(note => note.Sources)
            .HasForeignKey(source => source.NoteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class NoteLinkConfiguration : IEntityTypeConfiguration<NoteLinkEntity>
{
    public void Configure(EntityTypeBuilder<NoteLinkEntity> builder)
    {
        builder.ToTable(
            "note_links",
            table => table.HasCheckConstraint("ck_note_links_distinct", "note_id <> other_note_id"));
        builder.HasKey(link => new { link.NoteId, link.OtherNoteId });

        builder.HasOne(link => link.Note)
            .WithMany(note => note.OutgoingLinks)
            .HasForeignKey(link => link.NoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(link => link.Other)
            .WithMany()
            .HasForeignKey(link => link.OtherNoteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
