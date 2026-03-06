using FinCore.SharedKernel.Domain.Exceptions;

namespace FinCore.Identity.Domain.Exceptions;

public class UserAlreadyExistsException : DomainException
{
    public UserAlreadyExistsException(string email)
        : base($"A user with email '{email}' already exists.") { }
}
