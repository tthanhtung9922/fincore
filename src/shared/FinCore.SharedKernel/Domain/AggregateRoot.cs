namespace FinCore.SharedKernel.Domain;

public abstract class AggregateRoot
{
    public Guid Id { get; protected set; }
    public long Version { get; private set; }

    private readonly List<DomainEvent> _domainEvents = new();
    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseEvent(DomainEvent @event)
    {
        _domainEvents.Add(@event);
        Apply(@event);
        Version++;
    }

    protected virtual void Apply(DomainEvent @event) { }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
