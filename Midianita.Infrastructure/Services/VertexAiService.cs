using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Midianita.Core.Interfaces;

namespace Midianita.Infrastructure.Services
{
    public class VertexAiService : IVertexAiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _projectId;
        private readonly string _location;
        private readonly string _publisher = "google";
        private readonly string _model = "imagegeneration@005"; // Example model

        public VertexAiService(HttpClient httpClient, string projectId, string location = "us-central1")
        {
            _httpClient = httpClient;
            _projectId = projectId;
            _location = location;
        }

        public async Task<string> GenerateImageAsync(string prompt)
        {
            var credential = GoogleCredential.GetApplicationDefault();
            if (credential.IsCreateScopedRequired)
            {
                credential = credential.CreateScoped(new[] { "https://www.googleapis.com/auth/cloud-platform" });
            }
            var accessToken = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();

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
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            return responseString; // Rough return, in real world we'd parse the base64 image out
        }
    }
}
