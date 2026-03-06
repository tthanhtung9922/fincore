using FinCore.Identity.Domain.Entities;
using FinCore.Identity.Domain.Enums;
using FinCore.Identity.Domain.Events;
using FinCore.Identity.Domain.Exceptions;
using FinCore.Identity.Domain.ValueObjects;
using FinCore.SharedKernel.Domain;
using FinCore.SharedKernel.Domain.Exceptions;

namespace FinCore.Identity.Domain.Aggregates;

public class User : AggregateRoot
{
    public Email Email { get; private set; } = null!;
    public HashedPassword PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<RefreshToken> _refreshTokens = new();
    public IReadOnlyList<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    private User() { } // EF constructor

    public static User Register(Email email, HashedPassword passwordHash, UserRole role)
    {
        var user = new User();
        user.RaiseEvent(new UserRegistered(Guid.NewGuid(), email.Value, role.ToString()));
        user.Email = email;
        user.PasswordHash = passwordHash;
        user.Role = role;
        return user;
    }

    public void Login()
    {
        if (!IsActive)
            throw new UserDeactivatedException(Id);

        RaiseEvent(new UserLoggedIn(Id, DateTimeOffset.UtcNow));
    }

    public void AssignRole(UserRole newRole)
    {
        if (!IsActive)
            throw new UserDeactivatedException(Id);

        var oldRole = Role;
        RaiseEvent(new UserRoleChanged(Id, oldRole.ToString(), newRole.ToString()));
        Role = newRole;
    }

    public void Deactivate()
    {
        if (!IsActive)
            throw new DomainException($"User '{Id}' is already deactivated.");

        RaiseEvent(new UserDeactivated(Id, DateTimeOffset.UtcNow));
        IsActive = false;
    }

    public RefreshToken AddRefreshToken(string tokenHash, DateTimeOffset expiresAt)
    {
        var token = new RefreshToken(tokenHash, expiresAt);
        _refreshTokens.Add(token);
        return token;
    }

    public RefreshToken? GetActiveRefreshToken(string tokenHash) =>
        _refreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash && t.IsActive);

    public void RevokeAllRefreshTokens()
    {
        foreach (var token in _refreshTokens.Where(t => t.IsActive))
            token.Revoke();
    }

    protected override void Apply(DomainEvent @event)
    {
        if (@event is UserRegistered registered)
        {
            Id = registered.UserId;
            IsActive = true;
            CreatedAt = registered.OccurredAt;
        }
    }
}
