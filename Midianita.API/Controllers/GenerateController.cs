using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Midianita.Core.DTOs;
using Midianita.Core.Entities;
using Midianita.Core.Interfaces;
using System;
using System.Threading.Tasks;

namespace Midianita.API.Controllers
{
    /// <summary>
    /// API/Presentation Layer: Controller handling generation requests
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class GenerateController : ControllerBase
    {
        private readonly IDesignRepository _designRepository;
        private readonly ISafetyService _safetyService;
        private readonly IQueuePublisher _queuePublisher;
        private readonly IConfiguration _configuration;

        public GenerateController(
            IDesignRepository designRepository,
            ISafetyService safetyService,
            IQueuePublisher queuePublisher,
            IConfiguration configuration)
        {
            _designRepository = designRepository;
            _safetyService = safetyService;
            _queuePublisher = queuePublisher;
            _configuration = configuration;
        }

        [HttpPost("generate-image")]
        public async Task<IActionResult> GenerateImage([FromBody] GenerateImageRequest request)
        {
            // Validade the incoming DTO
            if (request == null)
            {
                return BadRequest("Invalid request body.");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Concatenate text
            var concatenatedText = $"{request.MainText} {request.SubText}".Trim();

            // Pass to ISafetyService
            if (!_safetyService.IsContentSafe(concatenatedText))
            {
                return BadRequest("The provided text violates our safety guidelines.");
            }

            // Save new DesignEntity with "Processing" status
            var designId = Guid.NewGuid();
            var designEntity = new DesignEntity
            {
                Id = designId,
                Title = request.MainText,
                UserId = "user-placeholder", // In a real app, extract from Claims/Identity
                Status = "Processing",
                CreatedAt = DateTime.UtcNow
            };

            await _designRepository.AddAsync(designEntity);

            // Fetch queue url from configuration
            var queueUrl = _configuration["AWS:ImageGenerationQueueUrl"];
            if (string.IsNullOrEmpty(queueUrl))
            {
                return StatusCode(500, "Queue URL is not configured properly.");
            }

            // Map data to ImageGenerationJob
            var job = new ImageGenerationJob
            {
                JobId = designId,
                BannerId = request.BannerId,
                MainText = request.MainText,
                SubText = request.SubText,
                Format = request.Format,
                ReferenceImageUrl = request.ReferenceImageUrl,
                CreatedAt = DateTime.UtcNow
            };

            // Publish job via IQueuePublisher
            await _queuePublisher.PublishAsync(job, queueUrl);

            // Return HTTP 202 Accepted with JobId
            return Accepted(new { JobId = designId, Status = "Processing" });
        }
    }
}
