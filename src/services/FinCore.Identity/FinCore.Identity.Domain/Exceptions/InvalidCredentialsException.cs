using FinCore.SharedKernel.Domain.Exceptions;

namespace FinCore.Identity.Domain.Exceptions;

public class InvalidCredentialsException : DomainException
{
    public InvalidCredentialsException()
        : base("Invalid email or password.") { }
}
