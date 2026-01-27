using Microsoft.AspNetCore.Mvc;
using Midianita.Aplication.Interface;
using Midianita.Aplication.ViewModel;

namespace Midianita.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DesignsController : ControllerBase
    {
        private readonly IDesignsService _designsService;

        public DesignsController(IDesignsService designsService)
        {
            _designsService = designsService;
        }

        [HttpPost]
        public async Task<IActionResult> Create(RequestDesign design)
        {
            var result = await _designsService.CreateAsync(design);

            if (result.Success)
            {
                return Ok(result.Data);
            }

            return BadRequest(result.Message);
        }
    }
}
