using System.Threading.Tasks;
using Midianita.Core.Entities;

namespace Midianita.Core.Interfaces
{
    /// <summary>
    /// Core/Application Layer: Repository interface for DesignEntity
    /// </summary>
    public interface IDesignRepository : IBaseRepository<Design>
    {
        public new Task CreateAsync(Design design);
        Task AddAsync(DesignEntity design);
    }
}
