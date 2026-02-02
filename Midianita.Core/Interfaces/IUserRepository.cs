using Midianita.Core.Entities;
using System.Threading.Tasks;

namespace Midianita.Core.Interfaces
{
    public interface IUserRepository
    {
        Task<User?> GetByEmailAsync(string email);
        Task<User?> GetByIdAsync(string id);
        Task SaveAsync(User user);
    }
}
