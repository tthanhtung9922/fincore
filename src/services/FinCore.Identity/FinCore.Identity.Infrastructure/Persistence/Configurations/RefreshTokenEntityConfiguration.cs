using FinCore.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinCore.Identity.Infrastructure.Persistence.Configurations;

public class RefreshTokenEntityConfiguration : IEntityTypeConfiguration<RefreshTokenEntity>
{
    public void Configure(EntityTypeBuilder<RefreshTokenEntity> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.Id).ValueGeneratedNever();
        builder.Property(rt => rt.UserId).IsRequired();
        builder.Property(rt => rt.TokenHash).IsRequired();
        builder.Property(rt => rt.ExpiresAt).IsRequired();
        builder.Property(rt => rt.IsRevoked).IsRequired();
        builder.Property(rt => rt.CreatedAt).IsRequired();
        builder.Property(rt => rt.ReplacedByToken).HasMaxLength(500);

        builder.HasIndex(rt => rt.TokenHash);
        builder.HasIndex(rt => rt.UserId);
    }
}
