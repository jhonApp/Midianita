using System.Security.Claims;

namespace Midianita.API.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        public static string GetUserId(this ClaimsPrincipal user)
        {
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(id))
            {
                id = user.FindFirstValue("sub");
            }

            return id ?? string.Empty;
        }

        public static string GetEmail(this ClaimsPrincipal user)
        {
            return user.FindFirstValue(ClaimTypes.Email);
        }
    }
}