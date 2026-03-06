using FinCore.SharedKernel.Domain.Exceptions;

namespace FinCore.Identity.Domain.Exceptions;

public class UserDeactivatedException : DomainException
{
    public UserDeactivatedException(Guid userId)
        : base($"User '{userId}' is deactivated and cannot perform this action.") { }
}
