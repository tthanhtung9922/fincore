using FinCore.SharedKernel.Domain;

namespace FinCore.Accounts.Domain.Events;

public record AccountFrozen(Guid AccountId, string Reason) : DomainEvent;
