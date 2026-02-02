using System.Threading;
using System.Threading.Tasks;

namespace Midianita.Worker.Interfaces
{
    public interface IQueueWorker
    {
        Task ExecuteAsync(CancellationToken stoppingToken);
    }
}
