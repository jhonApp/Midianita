using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Midianita.Aplication.DTOs;
using Midianita.Core.Interfaces;
using System;
using System.Threading.Tasks;

namespace Midianita.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                var user = await _authService.RegisterAsync(request.Email, request.Password);
                return Created("", new { user.Id, user.Email });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [EnableRateLimiting("login")]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var tokens = await _authService.LoginAsync(request.Email, request.Password);
                return Ok(new AuthResponse { AccessToken = tokens.AccessToken, RefreshToken = tokens.RefreshToken });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("Credenciais inválidas.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
        {
            try
            {
                var tokens = await _authService.RefreshTokenAsync(request.Token, request.RefreshToken);
                return Ok(new AuthResponse { AccessToken = tokens.AccessToken, RefreshToken = tokens.RefreshToken });
            }
            catch (UnauthorizedAccessException ex)
            {
                // Important: Return 401 so frontend knows to logout
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
