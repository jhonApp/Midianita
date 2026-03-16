using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
        private readonly IQueuePublisher _queuePublisher;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<DesignsService> _logger;

        public DesignsService(
            IDesignRepository designRepository,
            IAuditPublisher auditPublisher,
            IQueuePublisher queuePublisher,
            IConfiguration configuration,
            IHttpContextAccessor httpContextAccessor,
            ILogger<DesignsService> logger)
        {
            _designRepository = designRepository;
            _auditPublisher = auditPublisher;
            _queuePublisher = queuePublisher;
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
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

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id)
        {
            var design = await _designRepository.GetByIdAsync(id);
            if (design == null) return false;

            design.Status = "DELETED";
            await _designRepository.UpdateAsync(design);

            if (!string.IsNullOrEmpty(design.ImageUrl))
            {
                try
                {
                    var uri = new Uri(design.ImageUrl);
                    var s3Key = uri.AbsolutePath.TrimStart('/');
                    var cleanupMessage = new { s3Key };
                    var queueUrl = _configuration["AWS:CleanupQueueUrl"];
                    await _queuePublisher.PublishAsync(cleanupMessage, queueUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha ao enfileirar limpeza S3 para o Design {Id}. O arquivo pode ficar órfão.", id);
                }
            }

            return true;
        }

        public Task<IEnumerable<Design>> GetAllAsync()
        {
            throw new NotImplementedException();
        }

        public async Task<Design?> GetByIdAsync(string id)
        {
            return await _designRepository.GetByIdAsync(id);
        }

        public Task UpdateAsync(Design design)
        {
            throw new NotImplementedException();
        }
    }
}
