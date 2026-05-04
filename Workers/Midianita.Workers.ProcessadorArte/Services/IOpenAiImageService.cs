using Amazon.Lambda.Core;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Contrato para geração de banners compostos via OpenAI GPT Image 2.0 (gpt-image-1).
/// O modelo recebe a foto da pessoa + um prompt completo e retorna a imagem final
/// 100% composta — fundo, iluminação, sombras e textos renderizados pela IA.
/// </summary>
public interface IOpenAiImageService
{
    /// <summary>
    /// Envia a imagem de referência da pessoa e o prompt composto para a API da OpenAI
    /// e retorna a imagem final como um <see cref="Stream"/> pronto para upload no S3.
    /// </summary>
    /// <param name="compositePrompt">
    /// Prompt completo incluindo instruções de cenário, posicionamento da pessoa, textos e estilo.
    /// </param>
    /// <param name="personImageStream">
    /// Stream da imagem de referência da pessoa (JPEG ou PNG).
    /// Pode ser null se o banner não possuir recorte de pessoa — nesse caso o modelo
    /// gera apenas o cenário com textos.
    /// </param>
    /// <param name="personImageMimeType">
    /// MIME type da imagem da pessoa. Ex: "image/jpeg", "image/png".
    /// Ignorado se <paramref name="personImageStream"/> for null.
    /// </param>
    /// <param name="logger">Logger do contexto Lambda para rastreabilidade.</param>
    /// <param name="jobId">Identificador do job para telemetria e logs.</param>
    /// <returns>
    /// Stream com os bytes da imagem PNG final gerada pela OpenAI.
    /// O caller é responsável pelo dispose após o upload.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// Lançada quando a API retorna um status de erro HTTP (4xx/5xx),
    /// garantindo que o SQS execute o retry ou encaminhe para a DLQ.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Lançada quando a API rejeita o conteúdo (ex: violação de política de conteúdo)
    /// ou retorna uma resposta malformada sem os dados esperados.
    /// </exception>
    Task<Stream> GenerateComposedBannerAsync(
        string       compositePrompt,
        Stream?      personImageStream,
        string?      personImageMimeType,
        ILambdaLogger logger,
        string       jobId);
}
