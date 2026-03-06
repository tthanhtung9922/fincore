using FinCore.SharedKernel.Domain;

namespace FinCore.Accounts.Domain.Events;

public record AccountOpened(Guid AccountId, Guid OwnerId, string AccountType, string Currency, string AccountNumber) : DomainEvent;
