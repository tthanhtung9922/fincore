using FinCore.Accounts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Accounts.Infrastructure.Persistence;

public class AccountsDbContext : DbContext
{
    public AccountsDbContext(DbContextOptions<AccountsDbContext> options) : base(options) { }

    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccountsDbContext).Assembly);
    }
}
