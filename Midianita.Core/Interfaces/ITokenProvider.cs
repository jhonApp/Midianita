
namespace Midianita.Core.Interfaces
{
    public interface ITokenProvider
    {
        Task<string> GetAccessTokenAsync();
    }
}
