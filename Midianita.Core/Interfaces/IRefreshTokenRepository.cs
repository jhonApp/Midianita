using Midianita.Core.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Midianita.Core.Interfaces
{
    public interface IRefreshTokenRepository
    {
        Task<RefreshToken?> GetByTokenAsync(string token);
        Task SaveAsync(RefreshToken token);
        Task<IEnumerable<RefreshToken>> GetByUserIdAsync(string userId);
        Task RevokeAllForUserAsync(string userId);
    }
}
