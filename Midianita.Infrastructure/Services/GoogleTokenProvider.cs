using Google.Apis.Auth.OAuth2;
using Midianita.Core.Interfaces;

namespace Midianita.Infrastructure.Services
{
    public class GoogleTokenProvider : ITokenProvider
    {
        public async Task<string> GetAccessTokenAsync()
        {
            var credential = GoogleCredential.GetApplicationDefault();
            if (credential.IsCreateScopedRequired)
            {
                credential = credential.CreateScoped(new[] { "https://www.googleapis.com/auth/cloud-platform" });
            }
            return await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
        }
    }
}
