using FinCore.Identity.Application.Common;
using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Identity.Application.Commands.LoginUser;

public record LoginUserCommand(string Email, string Password) : IRequest<Result<AuthTokens>>;
