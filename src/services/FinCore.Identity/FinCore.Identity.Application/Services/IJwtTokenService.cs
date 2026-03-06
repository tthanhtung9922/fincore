using FinCore.Identity.Domain.Aggregates;

namespace FinCore.Identity.Application.Services;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    Guid? ValidateRefreshToken(string token);
}
