using FinCore.Identity.Application.Common;
using FinCore.Identity.Application.Services;
using FinCore.Identity.Domain.Exceptions;
using FinCore.Identity.Domain.Repositories;
using FinCore.Identity.Domain.ValueObjects;
using FinCore.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Options;

namespace FinCore.Identity.Application.Commands.LoginUser;

public class LoginUserCommandHandler : IRequestHandler<LoginUserCommand, Result<AuthTokens>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly JwtSettings _jwtSettings;

    public LoginUserCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IOptions<JwtSettings> jwtSettings)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _jwtSettings = jwtSettings.Value;
    }

    public async Task<Result<AuthTokens>> Handle(LoginUserCommand request, CancellationToken cancellationToken)
    {
        var email = new Email(request.Email);
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash.Value))
            return Result.Failure<AuthTokens>(new InvalidCredentialsException().Message);

        if (!user.IsActive)
            return Result.Failure<AuthTokens>(new UserDeactivatedException(user.Id).Message);

        user.Login();

        var accessToken = _jwtTokenService.GenerateAccessToken(user);
        var refreshTokenValue = _jwtTokenService.GenerateRefreshToken();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes);

        // Hash the refresh token for storage
        var tokenHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(refreshTokenValue)));

        var refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays);
        user.AddRefreshToken(tokenHash, refreshExpiresAt);

        await _userRepository.UpdateAsync(user, cancellationToken);
        user.ClearDomainEvents();

        return Result.Success(new AuthTokens(accessToken, refreshTokenValue, expiresAt));
    }
}
