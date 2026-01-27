using Midianita.Core.Entities;

namespace Midianita.Core.Interfaces
{
    public interface IDesignRepository : IBaseRepository<Design>
    {
        public new Task CreateAsync(Design design);
    }
}
