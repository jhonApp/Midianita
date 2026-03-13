namespace Midianita.Workers.ProcessadorArte.Models;

/// <summary>Represents a job record stored in DynamoDB.</summary>
public record JobEntity(
    string JobId,
    string BannerId,
    string Status,
    string? FinalS3Url = null,
    string? ErrorMessage = null
);
