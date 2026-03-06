using FinCore.Identity.Application.Common;
using FinCore.Identity.Application.Services;
using FinCore.Identity.Domain.Repositories;
using FinCore.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Options;

namespace FinCore.Identity.Application.Commands.RefreshToken;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<AuthTokens>>
{
    private readonly IUserRepository _userRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly JwtSettings _jwtSettings;

    public RefreshTokenCommandHandler(
        IUserRepository userRepository,
        IJwtTokenService jwtTokenService,
        IOptions<JwtSettings> jwtSettings)
    {
        _userRepository = userRepository;
        _jwtTokenService = jwtTokenService;
        _jwtSettings = jwtSettings.Value;
    }

    public async Task<Result<AuthTokens>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure<AuthTokens>("User not found.");

        var tokenHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(request.RefreshToken)));

        var existingToken = user.GetActiveRefreshToken(tokenHash);
        if (existingToken is null)
            return Result.Failure<AuthTokens>("Invalid or expired refresh token.");

        var newRefreshTokenValue = _jwtTokenService.GenerateRefreshToken();
        var newTokenHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(newRefreshTokenValue)));

        existingToken.Revoke(replacedByToken: newTokenHash);
        var refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays);
        user.AddRefreshToken(newTokenHash, refreshExpiresAt);

        var accessToken = _jwtTokenService.GenerateAccessToken(user);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes);

        await _userRepository.UpdateAsync(user, cancellationToken);

        return Result.Success(new AuthTokens(accessToken, newRefreshTokenValue, expiresAt));
    }
}
