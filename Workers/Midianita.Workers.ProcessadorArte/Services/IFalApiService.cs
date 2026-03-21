using Amazon.Lambda.Core;

namespace Midianita.Workers.ProcessadorArte.Services;

public interface IFalApiService
{
    Task<byte[]> GenerateImageAsync(
        List<string> imageUrls, 
        string masterPrompt, 
        ILambdaLogger logger,
        string jobId);
}
