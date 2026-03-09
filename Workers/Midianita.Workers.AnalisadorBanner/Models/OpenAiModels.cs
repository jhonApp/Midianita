using System.Text.Json.Serialization;

namespace Midianita.Workers.AnalisadorBanner.Models;

internal record OpenAiMessage(
    [property: JsonPropertyName("role")]    string Role,
    [property: JsonPropertyName("content")] object Content   // string OR list
);

internal record OpenAiImageUrl(
    [property: JsonPropertyName("url")] string Url
);

internal record OpenAiContentPart(
    [property: JsonPropertyName("type")]      string Type,
    [property: JsonPropertyName("text")]      string? Text     = null,
    [property: JsonPropertyName("image_url")] OpenAiImageUrl? ImageUrl = null
);

internal record OpenAiRequest(
    [property: JsonPropertyName("model")]      string Model,
    [property: JsonPropertyName("messages")]   List<OpenAiMessage> Messages,
    [property: JsonPropertyName("max_tokens")] int MaxTokens = 1024
);

internal record OpenAiChoice(
    [property: JsonPropertyName("message")] OpenAiMessage Message
);

internal record OpenAiResponse(
    [property: JsonPropertyName("choices")] List<OpenAiChoice> Choices
);
