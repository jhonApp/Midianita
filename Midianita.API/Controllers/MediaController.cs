using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Midianita.API.Extensions;
using Midianita.Core.DTOs;
using Midianita.Core.Interfaces;

namespace Midianita.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MediaController : ControllerBase
    {
        private readonly IQueuePublisher _publisher;

        public MediaController(IQueuePublisher publisher)
        {
            _publisher = publisher;
        }

        [HttpPost("generate-image")]
        public async Task<IActionResult> GenerateImage([FromBody] GenerateImageRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
                return BadRequest("O prompt é obrigatório.");

            var jobId = Guid.NewGuid();

            var job = new ImageGenerationJob
            {
                JobId = jobId,
                UserId = User.GetUserId(),
                Prompt = request.Prompt,
                CreatedAt = DateTime.UtcNow
            };

            await _publisher.PublishAsync(job, "GenerationQueueUrl");
            // Em vez de Console.WriteLine, use:
            System.Diagnostics.Debug.WriteLine("🚀 Enviando Job para fila SQS...");
            return Accepted(new { JobId = jobId, Status = "Processing" });
        }
    }
}
