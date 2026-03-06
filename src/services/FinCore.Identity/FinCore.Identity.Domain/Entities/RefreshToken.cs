using FinCore.SharedKernel.Domain;

namespace FinCore.Identity.Domain.Entities;

public class RefreshToken : Entity
{
    public string TokenHash { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? ReplacedByToken { get; private set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsActive => !IsRevoked && !IsExpired;

    private RefreshToken() { TokenHash = string.Empty; } // EF constructor

    public RefreshToken(string tokenHash, DateTimeOffset expiresAt)
    {
        Id = Guid.NewGuid();
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        IsRevoked = false;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void Revoke(string? replacedByToken = null)
    {
        IsRevoked = true;
        ReplacedByToken = replacedByToken;
    }
}
