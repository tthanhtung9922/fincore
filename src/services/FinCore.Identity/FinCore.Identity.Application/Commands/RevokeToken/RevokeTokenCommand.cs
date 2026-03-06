using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Identity.Application.Commands.RevokeToken;

public record RevokeTokenCommand(Guid UserId, string RefreshToken) : IRequest<Result>;
