using FinCore.EventBus.Abstractions;
using FinCore.Identity.Domain.Aggregates;
using FinCore.Identity.Domain.Enums;
using FinCore.Identity.Domain.Exceptions;
using FinCore.Identity.Domain.Repositories;
using FinCore.Identity.Domain.ValueObjects;
using FinCore.Identity.Application.Services;
using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Identity.Application.Commands.RegisterUser;

public class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, Result<Guid>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEventBus _eventBus;

    public RegisterUserCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IEventBus eventBus)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _eventBus = eventBus;
    }

    public async Task<Result<Guid>> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var email = new Email(request.Email);

        var existingUser = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (existingUser is not null)
            return Result.Failure<Guid>(new UserAlreadyExistsException(request.Email).Message);

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            return Result.Failure<Guid>($"Invalid role: {request.Role}");

        var hashedPassword = new HashedPassword(_passwordHasher.Hash(request.Password));
        var user = User.Register(email, hashedPassword, role);

        await _userRepository.AddAsync(user, cancellationToken);
        user.ClearDomainEvents();

        return Result.Success(user.Id);
    }
}
