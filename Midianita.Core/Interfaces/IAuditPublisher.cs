using System.Threading.Tasks;
using Midianita.Core.Entities;

namespace Midianita.Core.Interfaces
{
    public interface IAuditPublisher
    {
        Task PublishAsync(AuditLogEntry entry);
    }
}
