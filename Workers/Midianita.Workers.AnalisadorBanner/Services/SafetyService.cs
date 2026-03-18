using Amazon.Lambda.Core;
using System.Text.RegularExpressions;

namespace Midianita.Workers.AnalisadorBanner.Services;

public interface ISafetyService
{
    bool IsContentSafe(string userText, ILambdaLogger logger);
}

public class LocalSafetyService : ISafetyService
{
    // Basic set of patterns that could indicate prompt injection or system override attempts
    private static readonly string[] BadKeywords = 
    { 
        "ignore previous instructions", 
        "system prompt", 
        "dan mode", 
        "jailbreak",
        "<|endoftext|>", // Common token abuse
        "Forget about the church" 
    };

    public bool IsContentSafe(string userText, ILambdaLogger logger)
    {
        if (string.IsNullOrWhiteSpace(userText)) return true;

        // 1. Keyword check
        foreach (var keyword in BadKeywords)
        {
            if (userText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning($"[Safety] Potential injection detected: '{keyword}' in user input.");
                return false;
            }
        }

        // 2. Length check (protect SkiaSharp and LLM costs)
        if (userText.Length > 2000)
        {
             logger.LogWarning($"[Safety] Input text too long ({userText.Length} chars). Possible DoS.");
             return false;
        }

        return true;
    }
}
