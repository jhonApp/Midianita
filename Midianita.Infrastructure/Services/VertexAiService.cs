using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Midianita.Core.Interfaces;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Midianita.Infrastructure.Services
{
    public class VertexAiService : IVertexAiService
    {
        private readonly HttpClient _httpClient;
        private readonly ITokenProvider _tokenProvider;
        private readonly string _publisher = "google";
        private readonly string _model = "imagen-3.0-generate-001";
        private readonly IConfiguration _configuration;

        public VertexAiService(HttpClient httpClient, ITokenProvider tokenProvider, IConfiguration configuration)
        {
            _tokenProvider=tokenProvider;
            _configuration = configuration;
            _httpClient = httpClient;
        }

        public async Task<string> GenerateImageAsync(string prompt)
        {
            var projectId = _configuration["GCP:ProjectId"];
            var location = _configuration["GCP:Location"] ?? "us-central1";
            var accessToken = await _tokenProvider.GetAccessTokenAsync();

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var endpoint = $"https://{location}-aiplatform.googleapis.com/v1/projects/{projectId}/locations/{location}/publishers/{_publisher}/models/{_model}:predict";

            var payload = new
            {
                instances = new[]
                {
                    new { prompt = prompt }
                },
                parameters = new
                {
                    sampleCount = 1
                }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(endpoint, jsonContent);

            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Erro Vertex AI (HTTP {response.StatusCode}): {responseString}");
            }

            return responseString;
        }

        public async Task<string> GenerateTextAsync(string prompt)
        {
            var modelId = "gemini-2.0-flash-001";

            var projectId = _configuration["GCP:ProjectId"];
            var location = _configuration["GCP:Location"] ?? "us-central1";

            var url = $"https://{location}-aiplatform.googleapis.com/v1beta1/projects/{projectId}/locations/{location}/publishers/google/models/{modelId}:generateContent";

            var accessToken = await GetAccessTokenAsync();

            var fullPrompt = $"Você é um especialista em marketing de moda criativo. Crie uma frase curta, impactante e vendedora (máximo 10 palavras) em Português do Brasil baseada neste tema: '{prompt}'. Não use hashtags, não use aspas, retorne apenas o texto puro.";

            var requestBody = new
            {
                contents = new[]
                {
                    new 
                    { 
                        role = "user",
                        parts = new[] { new { text = fullPrompt } } 
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    maxOutputTokens = 50
                }
            };

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.PostAsync(url, new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ Erro Gemini: {error}");
                return "Seu estilo, sua moda.";
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(jsonResponse);

            try
            {
                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                return text?.Trim() ?? "";
            }
            catch
            {
                return "Moda incrível.";
            }
        }

        private async Task<string> GetAccessTokenAsync()
        {
            var jsonCreds = _configuration["GOOGLE_CREDENTIALS_JSON"]
                         ?? Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_JSON");

            GoogleCredential credential;

            if (!string.IsNullOrEmpty(jsonCreds))
            {
                credential = GoogleCredential.FromJson(jsonCreds);
            }
            else
            {
                credential = await GoogleCredential.GetApplicationDefaultAsync();
            }

            if (credential.IsCreateScopedRequired)
                credential = credential.CreateScoped(new[] { "https://www.googleapis.com/auth/cloud-platform" });

            var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
            return token;
        }
    }
}
