using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Identity.Application.Commands.RegisterUser;

public record RegisterUserCommand(string Email, string Password, string Role) : IRequest<Result<Guid>>;
