using Amazon.Lambda.Core;

namespace Midianita.Workers.ProcessadorArte.Services;

public interface IFalApiService
{
    Task<byte[]> GenerateImageAsync(
        string masterPrompt, 
        ILambdaLogger logger,
        string jobId);

    Task<byte[]> RemoveBackgroundAsync(
        byte[] imageBytes,
        ILambdaLogger logger,
        string jobId);
}
