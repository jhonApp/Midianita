using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Midianita.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public AuthController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpPost("login-mock")]
        public IActionResult GetLocalToken()
        {
            // 1. Verifica se estamos em modo Local
            var jwtSettings = _configuration.GetSection("JwtSettings");
            if (jwtSettings.GetValue<bool>("UseLocalKey") == false)
            {
                return BadRequest("Essa rota só funciona quando UseLocalKey = true");
            }

            // 2. Pega as configurações do appsettings.json
            var secretKey = jwtSettings["SecretKey"];
            var issuer = jwtSettings["Issuer"];
            var audience = jwtSettings["Audience"];

            // 3. Define quem é o usuário (Claims)
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "user-123"), // ID do Usuário
                new Claim(JwtRegisteredClaimNames.Email, "teste@midianita.local"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            // 4. Gera a assinatura (A Chave de Acesso)
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.Now.AddHours(1), // Token válido por 1 hora
                signingCredentials: creds
            );

            return Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token)
            });
        }
    }
}
