using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Midianita.Core.Interfaces;

namespace Midianita.Infrastructure.Services
{
    public class VertexAiService : IVertexAiService
    {
        private readonly HttpClient _httpClient;
        private readonly ITokenProvider _tokenProvider;
        private readonly string _projectId;
        private readonly string _location;
        private readonly string _publisher = "google";
        private readonly string _model = "imagen-3.0-generate-001";

        public VertexAiService(HttpClient httpClient, ITokenProvider tokenProvider, string projectId, string location)
        {
            _httpClient = httpClient;
            _tokenProvider = tokenProvider;
            _projectId = projectId;
            _location = location;
        }

        public async Task<string> GenerateImageAsync(string prompt)
        {
            var accessToken = await _tokenProvider.GetAccessTokenAsync();

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var endpoint = $"https://{_location}-aiplatform.googleapis.com/v1/projects/{_projectId}/locations/{_location}/publishers/{_publisher}/models/{_model}:predict";

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
    }
}
