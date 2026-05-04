using Amazon.Lambda.Core;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Integração com a API de edição de imagens do OpenAI GPT Image 2.0 (gpt-image-1).
///
/// Endpoint utilizado: POST https://api.openai.com/v1/images/edits
/// Documentação: https://platform.openai.com/docs/api-reference/images/createEdit
///
/// O modelo recebe:
///   - A imagem de referência da pessoa (multipart field "image[]")
///   - Um prompt textual completo com instruções de composição (field "prompt")
///   - O modelo "gpt-image-1", tamanho e qualidade desejados
///
/// A imagem final é retornada como base64 no campo "data[0].b64_json" e convertida
/// em Stream para upload direto no S3, sem intermediar um array de bytes em memória.
///
/// [FUTURO] Quando a API gpt-image-1 lançar suporte a "bounding_boxes" no payload
/// multipart, adicionar os campos coordenados de texto/pessoa aqui para substituir
/// as descrições em linguagem natural por posicionamento pixel-accurate.
/// Ref: https://platform.openai.com/docs/guides/image-generation (watch for updates)
/// </summary>
public sealed class OpenAiImageService : IOpenAiImageService
{
    private const string OpenAiEditsUrl  = "https://api.openai.com/v1/images/edits";
    private const string ModelName       = "gpt-image-2-2026-04-21";
    private const string OutputSize      = "1024x1536"; // Vertical banner (portrait 2:3)
    private const string OutputQuality   = "high";
    private const string OpenAiApiKeyEnv = "OPENAI_API_KEY";

    private readonly HttpClient        _httpClient;
    private readonly ITelemetryService _telemetry;

    public OpenAiImageService(HttpClient httpClient, ITelemetryService telemetry)
    {
        _httpClient = httpClient;
        _telemetry  = telemetry;
    }

    public async Task<Stream> GenerateComposedBannerAsync(
        string        compositePrompt,
        Stream?       personImageStream,
        string?       personImageMimeType,
        ILambdaLogger logger,
        string        jobId)
    {
        var sw      = Stopwatch.StartNew();
        var success = false;
        string? error = null;

        try
        {
            var apiKey = Environment.GetEnvironmentVariable(OpenAiApiKeyEnv)
                ?? throw new InvalidOperationException(
                    $"Variável de ambiente '{OpenAiApiKeyEnv}' não configurada.");

            logger.LogInformation(
                $"[OpenAiImageService] 🚀 Iniciando geração via {ModelName}. " +
                $"HasPersonImage: {personImageStream is not null}. " +
                $"PromptLength: {compositePrompt.Length} chars.");

            // ── Monta o payload multipart/form-data ───────────────────────────
            using var form = new MultipartFormDataContent();

            // Campo obrigatório: model
            form.Add(new StringContent(ModelName), "model");

            // Campo obrigatório: prompt
            form.Add(new StringContent(compositePrompt), "prompt");

            // Campo: size (1024x1536 para banner vertical, 1024x1024 para quadrado)
            // [FUTURO] Quando CanvasSize for passado como parâmetro, usar:
            //   form.Add(new StringContent(canvasSize.ToApiFormat()), "size");
            form.Add(new StringContent(OutputSize), "size");

            // Campo: quality (high = melhor qualidade, ideal para produção)
            form.Add(new StringContent(OutputQuality), "quality");

            // Campo: image[] — a imagem de referência da pessoa (opcional)
            // Quando presente, o modelo usa como referência espacial para compor a pessoa no cenário.
            // Quando ausente, o modelo gera apenas o cenário com textos (banner sem pessoa).
            if (personImageStream is not null)
            {
                var mimeType = personImageMimeType ?? "image/jpeg";

                // [NOTA] A API gpt-image-1 aceita "image[]" como array para múltiplas referências.
                // Atualmente enviamos apenas 1 imagem (a foto da pessoa).
                // [FUTURO] Adicionar suporte a múltiplas imagens de referência (ex: logo + pessoa)
                //   via loop e múltiplos campos "image[]".
                var imageContent = new StreamContent(personImageStream);
                imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);

                var fileName = mimeType.Contains("png") ? "person.png" : "person.jpg";
                form.Add(imageContent, "image[]", fileName);

                logger.LogInformation(
                    $"[OpenAiImageService] 📎 Imagem da pessoa anexada ao form ({mimeType}).");
            }

            // ── Chamada HTTP ──────────────────────────────────────────────────
            using var request = new HttpRequestMessage(HttpMethod.Post, OpenAiEditsUrl)
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", apiKey) },
                Content = form
            };

            logger.LogInformation("[OpenAiImageService] ⏳ Aguardando resposta da OpenAI...");

            // HttpClient.Timeout é configurado para 120s no DI (suficiente para gpt-image-1).
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            // ── Tratamento de erro HTTP ───────────────────────────────────────
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                logger.LogError(
                    $"[OpenAiImageService] ❌ OpenAI retornou erro HTTP {(int)response.StatusCode}. " +
                    $"Body: {errorBody}");

                // Lança para garantir retry via SQS / encaminhamento para DLQ
                throw new HttpRequestException(
                    $"OpenAI API error {(int)response.StatusCode}: {errorBody}");
            }

            // ── Parse da resposta JSON ────────────────────────────────────────
            var responseJson = await response.Content.ReadAsStringAsync();

            logger.LogInformation(
                $"[OpenAiImageService] ✅ Resposta recebida da OpenAI. Extraindo imagem...");

            using var doc = JsonDocument.Parse(responseJson);

            // Verifica se a API retornou erro de conteúdo (policy violation, etc.)
            if (doc.RootElement.TryGetProperty("error", out var errorElement))
            {
                var apiError = errorElement.TryGetProperty("message", out var msg)
                    ? msg.GetString()
                    : responseJson;

                logger.LogError($"[OpenAiImageService] ❌ OpenAI recusou o conteúdo: {apiError}");
                throw new InvalidOperationException($"OpenAI content policy violation: {apiError}");
            }

            // Extrai o base64 do campo data[0].b64_json
            // [NOTA] A API retorna base64 por padrão quando response_format não é especificado,
            // ou quando response_format="b64_json" é explicitamente enviado.
            // Isso é mais seguro que URLs temporárias que expiram após ~1 hora.
            if (!doc.RootElement.TryGetProperty("data", out var dataArray)
                || dataArray.ValueKind != JsonValueKind.Array
                || dataArray.GetArrayLength() == 0)
            {
                logger.LogError(
                    $"[OpenAiImageService] ❌ Payload inválido — campo 'data' ausente ou vazio. " +
                    $"Raw: {responseJson[..Math.Min(500, responseJson.Length)]}");
                throw new InvalidOperationException(
                    "OpenAI retornou resposta sem o campo 'data'. Verifique o schema da API.");
            }

            var firstItem = dataArray[0];

            if (!firstItem.TryGetProperty("b64_json", out var b64Element)
                || string.IsNullOrWhiteSpace(b64Element.GetString()))
            {
                logger.LogError(
                    $"[OpenAiImageService] ❌ Campo 'b64_json' ausente no item 0. " +
                    $"Keys disponíveis: {string.Join(", ", firstItem.EnumerateObject().Select(p => p.Name))}");
                throw new InvalidOperationException(
                    "OpenAI não retornou 'b64_json'. " +
                    "Certifique-se de que o modelo gpt-image-1 está ativo na sua conta.");
            }

            var base64 = b64Element.GetString()!;

            logger.LogInformation(
                $"[OpenAiImageService] 🖼️ Imagem gerada com sucesso. " +
                $"Base64 length: {base64.Length} chars (~{base64.Length * 3 / 4 / 1024} KB).");

            // Converte base64 → MemoryStream sem alocação dupla de byte[]
            // O MemoryStream é retornado para o caller (Function.cs) que faz o upload direto no S3.
            var imageBytes  = Convert.FromBase64String(base64);
            var imageStream = new MemoryStream(imageBytes) { Position = 0 };

            success = true;
            return imageStream;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            throw;
        }
        finally
        {
            sw.Stop();
            _telemetry.LogGenerationResult(jobId, ModelName, sw.ElapsedMilliseconds, success, error);
        }
    }
}
