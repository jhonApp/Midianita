
using System.Threading.Tasks;

namespace Midianita.Core.Interfaces
{
    public interface ITokenProvider
    {
        Task<string> GetAccessTokenAsync();
    }
}
