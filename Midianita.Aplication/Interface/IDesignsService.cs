using Midianita.Aplication.ViewModel;
using Midianita.Core.Entities;

namespace Midianita.Aplication.Interface
{
    public interface IDesignsService
    {
        Task<ResultOperation> CreateAsync(RequestDesign request);
        Task<Design?> GetByIdAsync(string id);
        Task<IEnumerable<Design>> GetAllAsync();
        Task UpdateAsync(Design design);
        Task DeleteAsync(string id);
    }
}
