using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Identity.Application.Queries.GetUserById;

public record GetUserByIdQuery(Guid UserId) : IRequest<Result<UserDto>>;
