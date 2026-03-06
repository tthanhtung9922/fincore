using FinCore.SharedKernel.Domain.Exceptions;

namespace FinCore.Accounts.Domain.Exceptions;

public class InsufficientFundsException : DomainException
{
    public InsufficientFundsException(decimal requested, decimal available, string currency)
        : base($"Insufficient funds. Requested: {requested:F2} {currency}, Available: {available:F2} {currency}.") { }
}
