using FinCore.Accounts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinCore.Accounts.Infrastructure.Persistence.Configurations;

public class AccountEntityConfiguration : IEntityTypeConfiguration<AccountEntity>
{
    public void Configure(EntityTypeBuilder<AccountEntity> builder)
    {
        builder.ToTable("accounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.OwnerId).IsRequired();
        builder.Property(a => a.AccountNumber).HasMaxLength(30).IsRequired();
        builder.Property(a => a.AccountType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.BalanceAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(a => a.BalanceCurrency).HasMaxLength(3).IsRequired();
        builder.Property(a => a.Currency).HasMaxLength(3).IsRequired();
        builder.Property(a => a.Status).HasMaxLength(20).IsRequired();
        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.Version).IsConcurrencyToken();

        builder.HasIndex(a => a.AccountNumber).IsUnique();
        builder.HasIndex(a => a.OwnerId);
    }
}
