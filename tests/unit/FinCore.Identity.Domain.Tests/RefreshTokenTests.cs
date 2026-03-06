using FinCore.Identity.Domain.Entities;
using FluentAssertions;

namespace FinCore.Identity.Domain.Tests;

public class RefreshTokenTests
{
    [Fact]
    public void Revoke_ActiveToken_SetsIsRevoked()
    {
        var token = new RefreshToken("hash123", DateTimeOffset.UtcNow.AddDays(7));

        token.Revoke();

        token.IsRevoked.Should().BeTrue();
        token.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_PastExpiry_ReturnsTrue()
    {
        var token = new RefreshToken("hash123", DateTimeOffset.UtcNow.AddSeconds(-1));

        token.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_FutureExpiry_ReturnsFalse()
    {
        var token = new RefreshToken("hash123", DateTimeOffset.UtcNow.AddDays(7));

        token.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void IsActive_NotRevokedNotExpired_ReturnsTrue()
    {
        var token = new RefreshToken("hash123", DateTimeOffset.UtcNow.AddDays(7));

        token.IsActive.Should().BeTrue();
    }
}
