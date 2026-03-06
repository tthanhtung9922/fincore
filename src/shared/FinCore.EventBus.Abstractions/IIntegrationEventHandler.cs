namespace FinCore.EventBus.Abstractions;

public interface IIntegrationEventHandler<in T> where T : IntegrationEvent
{
    Task HandleAsync(T integrationEvent, CancellationToken ct = default);
}
