using FinCore.SharedKernel.Domain;

namespace FinCore.Accounts.Domain.Events;

public record MoneyDeposited(Guid AccountId, decimal Amount, string Currency, string Reference) : DomainEvent;
