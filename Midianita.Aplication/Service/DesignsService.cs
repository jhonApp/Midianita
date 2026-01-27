using Microsoft.AspNetCore.Http;
using Midianita.Aplication.Interface;
using Midianita.Aplication.ViewModel;
using Midianita.Core.Entities;
using Midianita.Core.Interfaces;

namespace Midianita.Aplication.Service
{
    public class DesignsService : IDesignsService
    {
        private readonly IDesignRepository _designRepository;
        private readonly IAuditPublisher _auditPublisher;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public DesignsService(IDesignRepository designRepository, IAuditPublisher auditPublisher, IHttpContextAccessor httpContextAccessor)
        {
            _designRepository = designRepository;
            _auditPublisher = auditPublisher;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<ResultOperation> CreateAsync(RequestDesign request)
        {
            try
            {
                var newDesign = new Design
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = request.Name,
                    CanvasData = request.CanvasData,
                    Height = request.Height,
                    Width = request.Width,
                    Category = request.CategoryId,
                    CreatedAt = DateTime.Now,
                };

                await _designRepository.CreateAsync(newDesign);

                var auditLog = new AuditLogEntry
                {
                    LogId = Guid.NewGuid().ToString(),
                    Action = "CreateDesign",
                    UserId = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "Anonymous",
                    Details = $"Created design {newDesign.Id} ({newDesign.Name})",
                    Timestamp = DateTime.UtcNow
                };

                await _auditPublisher.PublishAsync(auditLog);

                return new ResultOperation { Success = true, Data = newDesign };
            }
            catch (Exception ex)
            {
                return new ResultOperation { Success = false, Message = ex.Message };
            }
        }

        public Task DeleteAsync(string id)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Design>> GetAllAsync()
        {
            throw new NotImplementedException();
        }

        public Task<Design?> GetByIdAsync(string id)
        {
            throw new NotImplementedException();
        }

        public Task UpdateAsync(Design design)
        {
            throw new NotImplementedException();
        }
    }
}
