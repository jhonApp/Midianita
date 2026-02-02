using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Midianita.API.Extensions
{
    public static class AuthExtensions
    {
        public static IServiceCollection AddDynamicAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            var jwtSettings = configuration.GetSection("JwtSettings");
            var useLocalKey = jwtSettings.GetValue<bool>("UseLocalKey");

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                if (useLocalKey)
                {
                    var secretKey = jwtSettings.GetValue<string>("SecretKey");
                    var issuer = jwtSettings.GetValue<string>("Issuer");
                    var audience = jwtSettings.GetValue<string>("Audience");

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = issuer,
                        ValidAudience = audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!))
                    };
                }
                else
                {
                    var authority = jwtSettings.GetValue<string>("Authority");
                    var audience = jwtSettings.GetValue<string>("Audience");

                    options.Authority = authority;
                    options.Audience = audience;

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateAudience = true,
                        NameClaimType = "sub" // Crucial for providers like Clerk/Auth0
                    };
                }
            });

            return services;
        }
    }
}
