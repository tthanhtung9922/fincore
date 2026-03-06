using FinCore.SharedKernel.Domain.Exceptions;

namespace FinCore.Accounts.Domain.Exceptions;

public class AccountAlreadyClosedException : DomainException
{
    public AccountAlreadyClosedException(Guid accountId)
        : base($"Account '{accountId}' is already closed.") { }
}
