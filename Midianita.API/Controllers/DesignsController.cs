using Microsoft.AspNetCore.Mvc;
using Midianita.Core.Entities;
using Midianita.Core.Interfaces;

namespace Midianita.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DesignsController : ControllerBase
    {
        private readonly IDesignRepository _repository;

        public DesignsController(IDesignRepository repository)
        {
            _repository = repository;
        }

        [HttpPost]
        public async Task<IActionResult> Create(Design design)
        {
            await _repository.CreateAsync(design);
            return CreatedAtAction(nameof(GetById), new { id = design.Id }, design);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var design = await _repository.GetByIdAsync(id);
            if (design == null)
            {
                return NotFound();
            }
            return Ok(design);
        }
    }
}
