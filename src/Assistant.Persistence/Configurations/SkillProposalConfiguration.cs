using Assistant.Core.Skills;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Configurations;

internal sealed class SkillProposalConfiguration : IEntityTypeConfiguration<SkillProposalEntity>
{
    public const int MaxContentLength = 65_536;

    public void Configure(EntityTypeBuilder<SkillProposalEntity> builder)
    {
        builder.ToTable("skill_proposals");
        builder.HasKey(proposal => proposal.Id);

        builder.Property(proposal => proposal.Name).HasMaxLength(64).IsRequired();
        builder.Property(proposal => proposal.Description).HasMaxLength(1024).IsRequired();
        builder.Property(proposal => proposal.Content).HasMaxLength(MaxContentLength).IsRequired();
        builder.Property(proposal => proposal.Status)
            .HasConversion(
                status => status.ToString().ToLowerInvariant(),
                value => Enum.Parse<SkillProposalStatus>(value, ignoreCase: true))
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(proposal => new { proposal.UserId, proposal.Name })
            .IsUnique()
            .HasFilter("status = 'pending'");

        builder.HasOne(proposal => proposal.User)
            .WithMany(user => user.SkillProposals)
            .HasForeignKey(proposal => proposal.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
