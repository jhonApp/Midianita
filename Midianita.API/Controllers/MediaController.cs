using Microsoft.AspNetCore.Mvc;
using Midianita.Core.Interfaces;

namespace Midianita.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MediaController : ControllerBase
    {
        private readonly IVertexAiService _vertexAiService;

        public MediaController(IVertexAiService vertexAiService)
        {
            _vertexAiService = vertexAiService;
        }

        [HttpPost("generate-image")]
        public async Task<IActionResult> GenerateImage([FromBody] string prompt)
        {
            try
            {
                var result = await _vertexAiService.GenerateImageAsync(prompt);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
