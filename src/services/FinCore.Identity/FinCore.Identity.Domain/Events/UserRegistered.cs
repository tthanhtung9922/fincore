using FinCore.SharedKernel.Domain;

namespace FinCore.Identity.Domain.Events;

public record UserRegistered(Guid UserId, string Email, string Role) : DomainEvent;
