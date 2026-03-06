using FinCore.Identity.Application.Common;
using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Identity.Application.Commands.RefreshToken;

public record RefreshTokenCommand(Guid UserId, string RefreshToken) : IRequest<Result<AuthTokens>>;
