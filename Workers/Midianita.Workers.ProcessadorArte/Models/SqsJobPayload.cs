using System.Text.Json.Serialization;

namespace Midianita.Workers.ProcessadorArte.Models;

/// <summary>Payload deserialized from the SQS message body.</summary>
public record SqsJobPayload(
    [property: JsonPropertyName("JobId")]       string JobId,
    [property: JsonPropertyName("BannerId")]    string BannerId,
    [property: JsonPropertyName("UserPhotoUrl")] string UserPhotoUrl,
    [property: JsonPropertyName("UserText")]    string UserText
);
