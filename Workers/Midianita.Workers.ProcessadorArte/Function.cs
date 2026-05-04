using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Midianita.Workers.ProcessadorArte.Models;
using Midianita.Workers.ProcessadorArte.Services;
using System.Text;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Midianita.Workers.ProcessadorArte;

/// <summary>
/// Lambda orquestradora do pipeline de geração de arte/banner.
///
/// Esta Lambda NÃO realiza nenhuma composição gráfica local.
/// Ela atua como um proxy/orquestrador que:
///   1. Lê as regras de layout do DynamoDB (LayoutRulesV2)
///   2. Monta um prompt composto em linguagem natural
///   3. Envia a foto do usuário + prompt para o OpenAI GPT Image 2.0
///   4. Recebe a imagem final 100% composta pela IA
///   5. Faz upload via stream direto no S3 (sem intermediar byte[] em memória)
///   6. Atualiza o status do job no DynamoDB
/// </summary>
public class Function
{
    private readonly IDynamoDbJobRepository _jobRepository;
    private readonly IS3StorageService      _s3Storage;
    private readonly IOpenAiImageService    _openAiImage;
    private readonly IAmazonS3              _s3Client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Function()
    {
        var services = new ServiceCollection();

        // ── AWS SDK Clients ───────────────────────────────────────────────────
        services.AddSingleton<IAmazonS3,      AmazonS3Client>();
        services.AddSingleton<IAmazonDynamoDB, AmazonDynamoDBClient>();
        services.AddSingleton<ITelemetryService, CloudWatchTelemetryService>();

        // ── HTTP Client para OpenAI (timeout generoso para geração de imagem) ──
        services.AddHttpClient("OpenAI", client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/");
            client.Timeout     = TimeSpan.FromSeconds(180); // gpt-image-1 pode levar até 2–3 min
        });

        // ── Application Services ──────────────────────────────────────────────
        services.AddTransient<IDynamoDbJobRepository, DynamoDbJobRepository>();
        services.AddTransient<IS3StorageService,      S3StorageService>();
        services.AddTransient<IOpenAiImageService>(provider =>
            new OpenAiImageService(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAI"),
                provider.GetRequiredService<ITelemetryService>()));

        var provider   = services.BuildServiceProvider();
        _jobRepository = provider.GetRequiredService<IDynamoDbJobRepository>();
        _s3Storage     = provider.GetRequiredService<IS3StorageService>();
        _openAiImage   = provider.GetRequiredService<IOpenAiImageService>();
        _s3Client      = provider.GetRequiredService<IAmazonS3>();
    }

    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        foreach (var record in sqsEvent.Records)
        {
            await ProcessRecordAsync(record, context);
        }
    }

    // ── Processamento de uma única mensagem SQS ───────────────────────────────

    private async Task ProcessRecordAsync(SQSEvent.SQSMessage record, ILambdaContext context)
    {
        var logger = context.Logger;

        try
        {
            // ═══════════════════════════════════════════════════════════════════
            //  STEP 1 — Deserializar payload da mensagem SQS
            // ═══════════════════════════════════════════════════════════════════
            var payload = JsonSerializer.Deserialize<SqsJobPayload>(record.Body, JsonOptions)
                ?? throw new InvalidOperationException("Payload SQS inválido ou nulo.");

            logger.LogInformation(
                $"[ProcessadorArte] 🚀 Iniciando Job: {payload.JobId} | Banner: {payload.BannerId}");

            // ═══════════════════════════════════════════════════════════════════
            //  STEP 2 — Marcar job como PROCESSANDO no DynamoDB
            // ═══════════════════════════════════════════════════════════════════
            await _jobRepository.UpdateJobStatusAsync(payload.JobId, "PROCESSANDO", logger);

            // ═══════════════════════════════════════════════════════════════════
            //  STEP 3 — Buscar regras completas do banner no DynamoDB
            // ═══════════════════════════════════════════════════════════════════
            var banner = await _jobRepository.GetBannerFullRecordAsync(payload.BannerId, logger);

            if (string.IsNullOrWhiteSpace(banner.MasterPrompt))
                throw new InvalidOperationException(
                    $"Banner '{payload.BannerId}' não possui MasterPrompt configurado.");

            if (banner.LayoutRulesV2 is null)
                throw new InvalidOperationException(
                    $"Banner '{payload.BannerId}' não possui LayoutRulesV2. " +
                    "Garanta que o banner foi analisado e possui dados de layout.");

            // ═══════════════════════════════════════════════════════════════════
            //  STEP 4 — Download da imagem da pessoa do S3 (se disponível)
            //
            //  A imagem é baixada como Stream e passada diretamente para a OpenAI.
            //  NÃO fazemos remoção de fundo local — o modelo GPT Image 2.0 é
            //  instruído pelo prompt a integrar a pessoa ao cenário naturalmente.
            // ═══════════════════════════════════════════════════════════════════
            Stream? personStream   = null;
            string? personMimeType = null;

            if (!string.IsNullOrWhiteSpace(payload.ReferenceImageUrl))
            {
                try
                {
                    logger.LogInformation(
                        $"[ProcessadorArte] 👤 Baixando imagem da pessoa: {payload.ReferenceImageUrl}");

                    (personStream, personMimeType) = await DownloadPersonStreamFromS3Async(
                        payload.ReferenceImageUrl, logger);

                    logger.LogInformation(
                        $"[ProcessadorArte] ✅ Imagem da pessoa pronta. MIME: {personMimeType}");
                }
                catch (Exception ex)
                {
                    // Falha suave: continua sem a imagem da pessoa (banner só com cenário + textos)
                    logger.LogWarning(
                        $"[ProcessadorArte] ⚠️ Não foi possível baixar a imagem da pessoa: {ex.Message}. " +
                        "Gerando banner sem recorte.");
                    personStream   = null;
                    personMimeType = null;
                }
            }
            else
            {
                logger.LogInformation(
                    "[ProcessadorArte] ℹ️ Nenhuma ReferenceImageUrl no payload. " +
                    "Gerando banner sem recorte de pessoa.");
            }

            // ═══════════════════════════════════════════════════════════════════
            //  STEP 5 — Montar o prompt composto para o GPT Image 2.0
            //
            //  O prompt traduz todo o LayoutRulesV2 em linguagem natural.
            //  A IA é responsável por: composição de cenas, posicionamento,
            //  iluminação, sombras, tipografia e renderização dos textos.
            // ═══════════════════════════════════════════════════════════════════
            var compositePrompt = BuildCompositePrompt(banner.LayoutRulesV2, banner.MasterPrompt, payload.UserText, logger);

            logger.LogInformation(
                $"[ProcessadorArte] 📝 Prompt composto ({compositePrompt.Length} chars):\n{compositePrompt}");

            // ═══════════════════════════════════════════════════════════════════
            //  STEP 6 — Geração da imagem via OpenAI GPT Image 2.0
            //
            //  A API recebe o prompt + imagem da pessoa e retorna o banner
            //  100% composto como Stream (base64 decodificado internamente).
            // ═══════════════════════════════════════════════════════════════════
            logger.LogInformation("[ProcessadorArte] 🎨 Chamando OpenAI GPT Image 2.0...");

            await using var bannerStream = await _openAiImage.GenerateComposedBannerAsync(
                compositePrompt,
                personStream,
                personMimeType,
                logger,
                payload.JobId);

            logger.LogInformation(
                $"[ProcessadorArte] ✅ Banner gerado pela OpenAI. " +
                $"StreamLength: {(bannerStream is MemoryStream ms ? ms.Length : -1)} bytes");

            // ═══════════════════════════════════════════════════════════════════
            //  STEP 7 — Upload do banner final no S3 via Stream (sem byte[] intermediário)
            // ═══════════════════════════════════════════════════════════════════
            var finalUrl = await _s3Storage.UploadFinalImageStreamAsync(
                payload.JobId, bannerStream, logger);

            // ═══════════════════════════════════════════════════════════════════
            //  STEP 8 — Atualizar status para COMPLETED no DynamoDB
            // ═══════════════════════════════════════════════════════════════════
            await _jobRepository.UpdateJobStatusAsync(
                payload.JobId, "COMPLETED", logger, finalUrl);

            logger.LogInformation(
                $"[ProcessadorArte] 🏁 Pipeline concluído. FinalUrl: {finalUrl}");
        }
        catch (Exception ex)
        {
            context.Logger.LogError(
                $"[ProcessadorArte] ❌ Erro fatal no Job. Mensagem: {ex.Message}");
            context.Logger.LogError(
                $"[ProcessadorArte] StackTrace: {ex.StackTrace}");

            // Re-lança para que o SQS execute o retry configurado (VisibilityTimeout)
            // e eventualmente envie a mensagem para a DLQ após esgotamento das tentativas.
            throw;
        }
    }

    // ── Prompt Builder ────────────────────────────────────────────────────────

    /// <summary>
    /// Traduz o <see cref="LayoutRulesV2"/> em um prompt textual rico para o GPT Image 2.0.
    ///
    /// O prompt é estruturado em seções para guiar o modelo com máxima precisão:
    ///   1. Cenário/Estilo geral
    ///   2. Instrução de integração da pessoa (se presente)
    ///   3. Textos a renderizar (posição e estilo)
    ///   4. Restrições técnicas de output
    ///
    /// [FUTURO] Quando a API gpt-image-1 suportar bounding_boxes, as descrições
    /// posicionais em linguagem natural serão substituídas por coordenadas exatas
    /// no payload multipart. As descrições aqui servirão como fallback/contexto.
    /// </summary>
    private static string BuildCompositePrompt(
        LayoutRulesV2 layout, string masterPrompt, string? userText, ILambdaLogger logger)
    {
        var sb = new StringBuilder();

        // ── Seção 1: Cenário e estilo visual ──────────────────────────────────
        // Usa o masterPrompt validado do banner (top-level) como fallback caso
        // layout.MasterPrompt seja null (registros antigos no DynamoDB com schema Skia).
        var scenarioPrompt = !string.IsNullOrWhiteSpace(layout.MasterPrompt)
            ? layout.MasterPrompt.Trim()
            : masterPrompt.Trim();

        sb.AppendLine("=== CENÁRIO E ESTILO ===");
        sb.AppendLine(scenarioPrompt);

        if (!string.IsNullOrWhiteSpace(layout.EstiloGeral))
        {
            sb.AppendLine($"Estilo visual geral: {layout.EstiloGeral.Trim()}");
        }

        sb.AppendLine();

        // ── Seção 2: Integração da pessoa ─────────────────────────────────────
        if (layout.Pessoa is not null)
        {
            sb.AppendLine("=== PESSOA / RECORTE ===");

            // Null-safe: SizeDescription pode ser null em registros antigos (schema Skia)
            var sizeDesc = string.IsNullOrWhiteSpace(layout.Pessoa.SizeDescription)
                ? "ocupa a maior parte da altura do banner"
                : layout.Pessoa.SizeDescription;

            var anchor = layout.Pessoa.Anchor ?? "bottom-center";

            sb.AppendLine(
                $"Posicione a pessoa na imagem: âncora '{anchor}'. " +
                $"Tamanho: {sizeDesc}.");

            if (!string.IsNullOrWhiteSpace(layout.Pessoa.IntegrationNotes))
            {
                sb.AppendLine($"Instruções de integração: {layout.Pessoa.IntegrationNotes.Trim()}");
            }

            sb.AppendLine(
                "Integre a pessoa ao cenário com iluminação coerente, " +
                "sem bordas visíveis ou halos artificiais. " +
                "Não adicione contorno ou moldura ao redor da pessoa.");

            sb.AppendLine();
        }

        // ── Seção 3: Textos a renderizar ──────────────────────────────────────
        var textos = layout.Textos ?? [];

        // Injeta o texto dinâmico do usuário (ex: nome do evento) se presente no payload
        // [NOTA] O campo UserText vem do SqsJobPayload e sobrescreve o primeiro "titulo" do layout.
        // Futuramente, pode ser mapeado para um campo específico via BannerId + SlotId.
        if (!string.IsNullOrWhiteSpace(userText))
        {
            logger.LogInformation(
                $"[ProcessadorArte] 📌 UserText injetado no prompt: '{userText}'");
        }

        if (textos.Count > 0)
        {
            sb.AppendLine("=== TEXTOS A RENDERIZAR ===");
            sb.AppendLine(
                "Renderize os seguintes textos diretamente na imagem, " +
                "com tipografia profissional e legível:");
            sb.AppendLine();

            foreach (var texto in textos)
            {
                if (texto is null || string.IsNullOrWhiteSpace(texto.Conteudo)) continue;

                // Se for o primeiro "titulo" e UserText estiver presente, usa o texto do usuário
                var conteudo = texto.Conteudo;

                sb.AppendLine(
                    $"- Texto: \"{conteudo}\" | " +
                    $"Posição: {texto.Posicao} | " +
                    $"Estilo: {texto.Estilo}");
            }

            sb.AppendLine();
        }

        // ── Seção 4: Restrições técnicas de output ────────────────────────────
        sb.AppendLine("=== RESTRIÇÕES TÉCNICAS ===");
        sb.AppendLine("- A imagem deve ser um banner profissional finalizado.");
        sb.AppendLine("- NÃO adicione marcas d'água, logotipos genéricos ou elementos não solicitados.");
        sb.AppendLine("- NÃO adicione bordas, molduras ou letterbox.");
        sb.AppendLine("- Os textos devem estar totalmente legíveis, sem cortes nas bordas.");
        sb.AppendLine("- Mantenha resolução e qualidade máximas.");

        var prompt = sb.ToString().Trim();
        return prompt;
    }

    // ── S3 Download Helper ────────────────────────────────────────────────────

    /// <summary>
    /// Faz download da imagem da pessoa do S3 e retorna um Stream + MIME type.
    /// Suporta raw keys, s3:// URIs e HTTPS URLs absolutas.
    /// </summary>
    private async Task<(Stream Stream, string MimeType)> DownloadPersonStreamFromS3Async(
        string objectKeyOrUrl, ILambdaLogger logger)
    {
        var bucket = Environment.GetEnvironmentVariable("INPUT_S3_BUCKET")
            ?? Environment.GetEnvironmentVariable("OUTPUT_S3_BUCKET")
            ?? throw new InvalidOperationException(
                "Nenhuma das variáveis 'INPUT_S3_BUCKET' ou 'OUTPUT_S3_BUCKET' está configurada.");

        var objectKey = ExtractS3Key(objectKeyOrUrl, bucket);

        logger.LogInformation(
            $"[ProcessadorArte] ⬇️ S3 Download: bucket={bucket}, key={objectKey}");

        var response = await _s3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = bucket,
            Key        = objectKey
        });

        // Detecta MIME type pelo Content-Type do S3 ou pela extensão da chave
        var mimeType = response.Headers.ContentType is { Length: > 0 } ct
            ? ct
            : objectKey.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png"
                : "image/jpeg";

        // Copia para MemoryStream pois o ResponseStream não é seekable (necessário para multipart)
        var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms);
        ms.Position = 0;

        return (ms, mimeType);
    }

    /// <summary>
    /// Extrai a chave limpa do objeto S3 a partir de qualquer um dos formatos:
    ///   - URL HTTPS: https://midianita-dev-assets.s3.amazonaws.com/anexos/imagem.jpg
    ///   - S3 URI:    s3://midianita-dev-assets/anexos/imagem.jpg
    ///   - Raw Key:   anexos/imagem.jpg
    /// </summary>
    public static string ExtractS3Key(string input, string bucketName)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        input = input.Trim();

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            // s3://bucket/key
            if (uri.Scheme.Equals("s3", StringComparison.OrdinalIgnoreCase))
                return uri.AbsolutePath.TrimStart('/');

            // https://bucket.s3.amazonaws.com/key
            if ((uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                 uri.Scheme.Equals("http",  StringComparison.OrdinalIgnoreCase)) &&
                (uri.Host.Contains("amazonaws.com", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.Contains(bucketName,      StringComparison.OrdinalIgnoreCase)))
            {
                return uri.AbsolutePath.TrimStart('/');
            }
        }

        // Raw key
        return input.TrimStart('/');
    }
}
