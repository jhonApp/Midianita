using System.Text.Json.Serialization;

namespace Midianita.Workers.ProcessadorArte.Models;

/// <summary>Payload deserialized from the SQS message body.</summary>
public record SqsJobPayload(
    [property: JsonPropertyName("JobId")]       string JobId,
    [property: JsonPropertyName("BannerId")]    string BannerId,
    [property: JsonPropertyName("ImageUrls")]    List<string> ImageUrls,
    [property: JsonPropertyName("ReferenceImageUrl")] string ReferenceImageUrl,
    [property: JsonPropertyName("UserText")]    string UserText,
    [property: JsonPropertyName("RemoveBackground")] bool RemoveBackground = true
);
