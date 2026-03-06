using FinCore.SharedKernel.Domain.Exceptions;

namespace FinCore.Accounts.Domain.Exceptions;

public class AccountNotActiveException : DomainException
{
    public AccountNotActiveException(Guid accountId, string status)
        : base($"Account '{accountId}' is not active (current status: {status}).") { }
}
