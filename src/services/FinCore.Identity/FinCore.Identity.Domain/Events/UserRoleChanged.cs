using FinCore.SharedKernel.Domain;

namespace FinCore.Identity.Domain.Events;

public record UserRoleChanged(Guid UserId, string OldRole, string NewRole) : DomainEvent;
