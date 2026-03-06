using FinCore.SharedKernel.Domain;

namespace FinCore.Identity.Domain.Events;

public record UserDeactivated(Guid UserId, DateTimeOffset At) : DomainEvent;
