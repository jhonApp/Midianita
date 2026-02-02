namespace Midianita.Core.Interfaces
{
    public interface IQueuePublisher
    {
        Task PublishAsync<T>(T message, string queueNameConfigKey);
    }
}
