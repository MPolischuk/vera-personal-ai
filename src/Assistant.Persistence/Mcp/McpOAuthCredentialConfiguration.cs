using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Persistence.Mcp;

internal sealed class McpOAuthCredentialConfiguration : IEntityTypeConfiguration<McpOAuthCredentialEntity>
{
    public void Configure(EntityTypeBuilder<McpOAuthCredentialEntity> builder)
    {
        builder.ToTable("mcp_oauth_credentials", table =>
        {
            table.HasCheckConstraint(
                "ck_mcp_oauth_credentials_refresh",
                "char_length(refresh_protected) BETWEEN 1 AND 4000");
            table.HasCheckConstraint("ck_mcp_oauth_credentials_server", "char_length(server_id) BETWEEN 1 AND 64");
        });
        builder.HasKey(credential => new { credential.UserId, credential.ServerId });
        builder.Property(credential => credential.ServerId).HasMaxLength(64).IsRequired();
        builder.Property(credential => credential.RefreshProtected).HasMaxLength(4000).IsRequired();
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(credential => credential.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
