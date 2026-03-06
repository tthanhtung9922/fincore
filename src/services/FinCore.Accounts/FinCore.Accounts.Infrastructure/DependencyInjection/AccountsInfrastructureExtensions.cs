using FinCore.Accounts.Application.Repositories;
using FinCore.Accounts.Domain.Repositories;
using FinCore.Accounts.Infrastructure.Persistence;
using FinCore.Accounts.Infrastructure.Persistence.ReadModels;
using FinCore.Accounts.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Accounts.Infrastructure.DependencyInjection;

public static class AccountsInfrastructureExtensions
{
    public static IServiceCollection AddAccountsInfrastructure(
        this IServiceCollection services,
        IConfiguration config)
    {
        var connectionString = Environment.GetEnvironmentVariable("DB__ACCOUNTS")
            ?? config.GetConnectionString("AccountsDb")
            ?? "Host=localhost;Port=5433;Database=fincore_accounts;Username=fincore;Password=fincore_dev";

        services.AddDbContext<AccountsDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IAccountRepository, EfAccountRepository>();
        services.AddScoped<IAccountReadRepository, AccountReadRepository>();

        return services;
    }
}
