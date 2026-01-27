using Midianita.Core.Entities;

namespace Midianita.Core.Interfaces
{
    public interface IBaseRepository<TEntity> where TEntity : class
    {
        Task CreateAsync(Design design);
        Task<Design?> GetByIdAsync(string id);
        Task<IEnumerable<Design>> GetAllAsync();
        Task UpdateAsync(Design design);
        Task DeleteAsync(string id);
    }
}
