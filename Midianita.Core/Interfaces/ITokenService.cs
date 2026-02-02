using Midianita.Core.Entities;

namespace Midianita.Core.Interfaces
{
    public interface ITokenService
    {
        string GenerateAccessToken(User user);
        string GenerateRefreshToken();
    }
}
