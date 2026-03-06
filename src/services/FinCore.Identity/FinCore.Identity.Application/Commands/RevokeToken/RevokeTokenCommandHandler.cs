using FinCore.Identity.Domain.Repositories;
using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Identity.Application.Commands.RevokeToken;

public class RevokeTokenCommandHandler : IRequestHandler<RevokeTokenCommand, Result>
{
    private readonly IUserRepository _userRepository;

    public RevokeTokenCommandHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<Result> Handle(RevokeTokenCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure("User not found.");

        var tokenHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(request.RefreshToken)));

        var token = user.GetActiveRefreshToken(tokenHash);
        if (token is null)
            return Result.Failure("Token not found or already revoked.");

        token.Revoke();
        await _userRepository.UpdateAsync(user, cancellationToken);

        return Result.Success();
    }
}
