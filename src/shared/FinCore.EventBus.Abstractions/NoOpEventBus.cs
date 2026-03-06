namespace FinCore.EventBus.Abstractions;

public sealed class NoOpEventBus : IEventBus
{
    public Task PublishAsync<T>(T integrationEvent, CancellationToken ct = default) where T : IntegrationEvent
        => Task.CompletedTask;
}
