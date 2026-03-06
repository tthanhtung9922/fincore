using FinCore.Identity.Domain.Repositories;
using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Identity.Application.Queries.GetUserById;

public class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, Result<UserDto>>
{
    private readonly IUserRepository _userRepository;

    public GetUserByIdQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<Result<UserDto>> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure<UserDto>("User not found.");

        return Result.Success(new UserDto(
            user.Id,
            user.Email.Value,
            user.Role.ToString(),
            user.IsActive,
            user.CreatedAt));
    }
}
