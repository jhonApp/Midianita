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
        private readonly IDesignRepository _designRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MediaController> _logger;

        public MediaController(IQueuePublisher publisher, IDesignRepository designRepository, IConfiguration configuration, ILogger<MediaController> logger)
        {
            _publisher = publisher;
            _designRepository = designRepository;
            _configuration = configuration;
            _logger = logger;
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

            return Accepted(new { JobId = jobId, Status = "Processing" });
        }

        [HttpDelete("designs/{id}")]
        public async Task<IActionResult> DeleteDesign(string id)
        {
            var design = await _designRepository.GetByIdAsync(id);
            if (design == null) return NotFound();

            design.Status = "DELETED";
            await _designRepository.UpdateAsync(design);

            if (!string.IsNullOrEmpty(design.ImageUrl))
            {
                try
                {
                    var uri = new Uri(design.ImageUrl);

                    var s3Key = uri.AbsolutePath.TrimStart('/');

                    var cleanupMessage = new { s3Key = s3Key };

                    var queueUrl = _configuration["AWS:CleanupQueueUrl"];

                    await _publisher.PublishAsync(cleanupMessage, queueUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha ao enfileirar limpeza S3 para o Design {Id}. O arquivo pode ficar órfão.", id);
                }
            }

            return NoContent();
        }
    }
}
