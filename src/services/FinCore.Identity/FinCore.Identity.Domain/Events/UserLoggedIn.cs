using FinCore.SharedKernel.Domain;

namespace FinCore.Identity.Domain.Events;

public record UserLoggedIn(Guid UserId, DateTimeOffset At) : DomainEvent;
