using Microsoft.AspNetCore.Mvc.Filters;
using Midianita.Core.Entities;
using Midianita.Core.Interfaces;

namespace Midianita.API.Filters
{
    public class AuditActionFilter : IAsyncActionFilter
    {
        private readonly IAuditPublisher _auditPublisher;

        public AuditActionFilter(IAuditPublisher auditPublisher)
        {
            _auditPublisher = auditPublisher;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var executedContext = await next();

            var entry = new AuditLogEntry
            {
                Action = context.ActionDescriptor.DisplayName ?? "Unknown",
                UserId = context.HttpContext.User.Identity?.Name ?? "Anonymous",
                Details = $"Path: {context.HttpContext.Request.Path}, StatusCode: {executedContext.Result?.GetType().Name}"
            };

            await _auditPublisher.PublishAsync(entry);
        }
    }
}
