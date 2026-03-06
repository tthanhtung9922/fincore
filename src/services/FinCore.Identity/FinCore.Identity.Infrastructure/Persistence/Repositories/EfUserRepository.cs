using FinCore.Identity.Domain.Aggregates;
using FinCore.Identity.Domain.Entities;
using FinCore.Identity.Domain.Repositories;
using FinCore.Identity.Domain.ValueObjects;
using FinCore.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Identity.Infrastructure.Persistence.Repositories;

public class EfUserRepository : IUserRepository
{
    private readonly IdentityDbContext _context;

    public EfUserRepository(IdentityDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        return entity is null ? null : MapToDomain(entity);
    }

    public async Task<User?> GetByEmailAsync(Email email, CancellationToken ct = default)
    {
        var entity = await _context.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Email == email.Value, ct);

        return entity is null ? null : MapToDomain(entity);
    }

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        var entity = MapToEntity(user);
        await _context.Users.AddAsync(entity, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        var entity = await _context.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == user.Id, ct);

        if (entity is null) return;

        entity.Email = user.Email.Value;
        entity.PasswordHash = user.PasswordHash.Value;
        entity.Role = user.Role.ToString();
        entity.IsActive = user.IsActive;
        entity.Version = user.Version;

        // Sync refresh tokens
        var existingTokenIds = entity.RefreshTokens.Select(t => t.Id).ToHashSet();
        foreach (var domainToken in user.RefreshTokens)
        {
            var existing = entity.RefreshTokens.FirstOrDefault(t => t.Id == domainToken.Id);
            if (existing is null)
            {
                entity.RefreshTokens.Add(new RefreshTokenEntity
                {
                    Id = domainToken.Id,
                    UserId = user.Id,
                    TokenHash = domainToken.TokenHash,
                    ExpiresAt = domainToken.ExpiresAt,
                    IsRevoked = domainToken.IsRevoked,
                    CreatedAt = domainToken.CreatedAt,
                    ReplacedByToken = domainToken.ReplacedByToken
                });
            }
            else
            {
                existing.IsRevoked = domainToken.IsRevoked;
                existing.ReplacedByToken = domainToken.ReplacedByToken;
            }
        }

        await _context.SaveChangesAsync(ct);
    }

    private static User MapToDomain(UserEntity entity)
    {
        var email = new Email(entity.Email);
        var passwordHash = new HashedPassword(entity.PasswordHash);

        if (!Enum.TryParse<FinCore.Identity.Domain.Enums.UserRole>(entity.Role, out var role))
            role = FinCore.Identity.Domain.Enums.UserRole.Customer;

        var user = User.Register(email, passwordHash, role);

        // Use reflection to set private fields for reconstitution
        var idProp = typeof(FinCore.SharedKernel.Domain.AggregateRoot)
            .GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        idProp?.SetValue(user, entity.Id);

        // Clear events raised by Register factory
        user.ClearDomainEvents();

        // Set IsActive
        if (!entity.IsActive)
        {
            typeof(User)
                .GetProperty("IsActive", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                ?.SetValue(user, entity.IsActive);
        }

        // Set CreatedAt
        typeof(User)
            .GetProperty("CreatedAt", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            ?.SetValue(user, entity.CreatedAt);

        // Add refresh tokens
        foreach (var rt in entity.RefreshTokens)
        {
            var token = new RefreshToken(rt.TokenHash, rt.ExpiresAt);
            typeof(FinCore.SharedKernel.Domain.Entity)
                .GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                ?.SetValue(token, rt.Id);

            if (rt.IsRevoked)
                token.Revoke(rt.ReplacedByToken);

            var tokensField = typeof(User)
                .GetField("_refreshTokens", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            ((List<RefreshToken>?)tokensField?.GetValue(user))?.Add(token);
        }

        return user;
    }

    private static UserEntity MapToEntity(User user) => new()
    {
        Id = user.Id,
        Email = user.Email.Value,
        PasswordHash = user.PasswordHash.Value,
        Role = user.Role.ToString(),
        IsActive = user.IsActive,
        CreatedAt = user.CreatedAt,
        Version = user.Version,
        RefreshTokens = user.RefreshTokens.Select(rt => new RefreshTokenEntity
        {
            Id = rt.Id,
            UserId = user.Id,
            TokenHash = rt.TokenHash,
            ExpiresAt = rt.ExpiresAt,
            IsRevoked = rt.IsRevoked,
            CreatedAt = rt.CreatedAt,
            ReplacedByToken = rt.ReplacedByToken
        }).ToList()
    };
}
