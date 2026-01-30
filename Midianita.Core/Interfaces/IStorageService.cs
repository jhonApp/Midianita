
using System.IO;
using System.Threading.Tasks;

namespace Midianita.Core.Interfaces
{
    public interface IStorageService
    {
        Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType);
    }
}
