using Midianita.Core.Interfaces;
using System;

namespace Midianita.Infrastructure.Services
{
    public class SafetyService : ISafetyService
    {
        private static readonly string[] BadKeywords = 
        { 
            "ignore previous instructions", 
            "system prompt", 
            "dan mode", 
            "jailbreak",
            "<|endoftext|>"
        };

        public bool IsContentSafe(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return true;

            foreach (var keyword in BadKeywords)
            {
                if (content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (content.Length > 2000)
            {
                 return false;
            }

            return true;
        }
    }
}
