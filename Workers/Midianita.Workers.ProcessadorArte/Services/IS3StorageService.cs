using Amazon.Lambda.Core;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Uploads the final composed image to S3 and returns the public URL.
/// </summary>
public interface IS3StorageService
{
    /// <summary>
    /// Faz upload da imagem final a partir de um <see cref="Stream"/>, sem carregar
    /// o array de bytes completo na memória da Lambda.
    /// Recomendado para imagens geradas por IA (tipicamente 2–5 MB).
    /// </summary>
    Task<string> UploadFinalImageStreamAsync(string jobId, Stream imageStream, ILambdaLogger logger);

    /// <summary>
    /// Sobrecarga legada para upload a partir de byte[].
    /// Prefira <see cref="UploadFinalImageStreamAsync"/> em novos fluxos.
    /// </summary>
    Task<string> UploadFinalImageAsync(string jobId, byte[] imageBytes, ILambdaLogger logger);
}
