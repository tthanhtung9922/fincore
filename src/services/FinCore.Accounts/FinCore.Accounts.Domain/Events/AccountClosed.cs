using FinCore.SharedKernel.Domain;

namespace FinCore.Accounts.Domain.Events;

public record AccountClosed(Guid AccountId, DateTimeOffset At) : DomainEvent;
