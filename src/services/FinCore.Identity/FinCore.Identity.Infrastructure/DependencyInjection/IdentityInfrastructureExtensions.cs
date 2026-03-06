using FinCore.Identity.Application.Services;
using FinCore.Identity.Domain.Repositories;
using FinCore.Identity.Infrastructure.Persistence;
using FinCore.Identity.Infrastructure.Persistence.Repositories;
using FinCore.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Identity.Infrastructure.DependencyInjection;

public static class IdentityInfrastructureExtensions
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration config)
    {
        var connectionString = Environment.GetEnvironmentVariable("DB__IDENTITY")
            ?? config.GetConnectionString("IdentityDb")
            ?? "Host=localhost;Port=5433;Database=fincore_identity;Username=fincore;Password=fincore_dev";

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.Configure<JwtSettings>(options =>
        {
            options.Secret = Environment.GetEnvironmentVariable("JWT__SECRET") ?? "dev-secret-must-be-at-least-32-chars!!";
            options.Issuer = Environment.GetEnvironmentVariable("JWT__ISSUER") ?? "fincore-identity";
            options.Audience = Environment.GetEnvironmentVariable("JWT__AUDIENCE") ?? "fincore";

            if (int.TryParse(Environment.GetEnvironmentVariable("JWT__ACCESS_TOKEN_EXPIRY_MINUTES"), out var accessExpiry))
                options.AccessTokenExpiryMinutes = accessExpiry;

            if (int.TryParse(Environment.GetEnvironmentVariable("JWT__REFRESH_TOKEN_EXPIRY_DAYS"), out var refreshExpiry))
                options.RefreshTokenExpiryDays = refreshExpiry;
        });

        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
