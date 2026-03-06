namespace FinCore.Identity.Infrastructure.Persistence.Entities;

public class RefreshTokenEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? ReplacedByToken { get; set; }
    public UserEntity User { get; set; } = null!;
}
