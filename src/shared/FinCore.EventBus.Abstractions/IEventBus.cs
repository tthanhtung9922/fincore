namespace FinCore.EventBus.Abstractions;

public interface IEventBus
{
    Task PublishAsync<T>(T integrationEvent, CancellationToken ct = default) where T : IntegrationEvent;
}
