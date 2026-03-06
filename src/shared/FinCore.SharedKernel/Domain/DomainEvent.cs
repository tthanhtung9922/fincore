namespace FinCore.SharedKernel.Domain;

public abstract record DomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public int SchemaVersion { get; init; } = 1;
}
