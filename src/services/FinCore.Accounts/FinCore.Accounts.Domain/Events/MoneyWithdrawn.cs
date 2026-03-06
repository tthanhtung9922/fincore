using FinCore.SharedKernel.Domain;

namespace FinCore.Accounts.Domain.Events;

public record MoneyWithdrawn(Guid AccountId, decimal Amount, string Currency, string Reference) : DomainEvent;
