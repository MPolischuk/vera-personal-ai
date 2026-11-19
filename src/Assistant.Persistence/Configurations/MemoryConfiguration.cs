using System.Text.Json;
using Assistant.Core.Memory;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace Assistant.Persistence.Configurations;

internal sealed class MemoryConfiguration : IEntityTypeConfiguration<MemoryEntity>
{
    public void Configure(EntityTypeBuilder<MemoryEntity> builder)
    {
        builder.ToTable("memories");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Kind)
            .HasConversion(
                kind => kind.ToString().ToLowerInvariant(),
                value => Enum.Parse<MemoryKind>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(m => m.WriteProvenance)
            .HasConversion(
                value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()),
                value => Enum.Parse<WriteProvenance>(
                    value.Replace("_", "", StringComparison.Ordinal), ignoreCase: true))
            .HasMaxLength(40)
            .IsRequired()
            .HasDefaultValue(WriteProvenance.ModelClean);

        builder.Property(m => m.Pinned)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(m => m.IndexTerms).HasColumnType("text");
        builder.Property(m => m.DeletionReason).HasMaxLength(16);
        builder.PrimitiveCollection(m => m.DerivedFrom)
            .HasColumnType("uuid[]");
        builder.Property(m => m.EmbeddingModel).IsRequired();
        builder.Property(m => m.Embedding)
            .HasColumnType($"vector({MemorySchema.EmbeddingDimensions})");

        builder.Property(m => m.ValidPeriod)
            .HasColumnType("tstzrange")
            .HasDefaultValueSql("tstzrange(CURRENT_TIMESTAMP, 'infinity', '[)')")
            .HasSentinel(default(NpgsqlRange<DateTime>));

        builder.HasIndex(m => m.UserId)
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(m => m.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops")
            .HasFilter("deleted_at IS NULL AND superseded_by IS NULL");

        builder.HasOne(m => m.User)
            .WithMany(u => u.Memories)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(m => m.DeletedAt == null);
    }
}
