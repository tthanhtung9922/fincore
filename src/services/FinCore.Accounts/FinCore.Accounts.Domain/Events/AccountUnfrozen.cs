using FinCore.SharedKernel.Domain;

namespace FinCore.Accounts.Domain.Events;

public record AccountUnfrozen(Guid AccountId) : DomainEvent;
