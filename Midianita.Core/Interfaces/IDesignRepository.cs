using System.Threading.Tasks;
using Midianita.Core.Entities;

namespace Midianita.Core.Interfaces
{
    /// <summary>
    /// Core/Application Layer: Repository interface for DesignEntity
    /// </summary>
    public interface IDesignRepository
    {
        Task AddAsync(DesignEntity design);
    }
}
