using Microsoft.AspNetCore.Mvc;
using Midianita.Core.DTOs;
using Midianita.Core.Interfaces;

namespace Midianita.API.Controllers
{
    /// <summary>
    /// Handles banner layout analysis requests using the Claim Check (async) pattern.
    /// POST  /api/analysis/analyze     → Starts analysis, returns 202 + BannerId
    /// GET   /api/analysis/{id}/status → Polls for result
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AnalysisController : ControllerBase
    {
        private readonly IBannerRepository _bannerRepository;
        private readonly ISqsPublisher _sqsPublisher;

        public AnalysisController(
            IBannerRepository bannerRepository,
            ISqsPublisher sqsPublisher)
        {
            _bannerRepository = bannerRepository;
            _sqsPublisher = sqsPublisher;
        }

        /// <summary>
        /// Receives a reference banner image URL, creates a tracking record in DynamoDB,
        /// publishes a lightweight message to SQS, and returns HTTP 202 immediately.
        /// The actual AI analysis runs asynchronously on the AnalisadorBanner Lambda.
        /// </summary>
        [HttpPost("analyze")]
        public async Task<IActionResult> Analyze([FromBody] AnalyzeBannerRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var bannerId = Guid.NewGuid();

            // 1. Persist initial record with ANALYZING status
            await _bannerRepository.SaveAsync(bannerId, request.ReferenceImageUrl);

            // 2. Publish Claim Check message — only the BannerId travels through SQS
            await _sqsPublisher.PublishToAnalysisQueueAsync(new AnalysisJobMessage
            {
                BannerId = bannerId
            });

            // 3. Return 202 Accepted with polling URL
            var statusUrl = Url.Action(nameof(GetStatus), "Analysis", new { bannerId }, Request.Scheme);

            return Accepted(new
            {
                BannerId = bannerId,
                Message = "Analysis started. Use the status URL to check progress.",
                StatusUrl = statusUrl
            });
        }

        /// <summary>
        /// Polling endpoint — the front-end calls this periodically until Status == "ANALYZED".
        /// Once complete, the response includes the LayoutRulesV2 JSON payload.
        /// </summary>
        [HttpGet("{bannerId}/status")]
        public async Task<IActionResult> GetStatus([FromRoute] Guid bannerId)
        {
            var record = await _bannerRepository.GetByIdAsync(bannerId);

            if (record is null)
                return NotFound(new { Message = $"Banner {bannerId} not found." });

            return Ok(new
            {
                record.BannerId,
                record.Status,
                LayoutRulesV2 = record.LayoutRulesV2Json
            });
        }
    }
}
