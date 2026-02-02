using Midianita.Aplication.DTOs;
using Midianita.Core.Entities;
using Midianita.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Midianita.Aplication.Service
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly ICryptographyService _cryptoService;
        private readonly ITokenService _tokenService;

        public AuthService(
            IUserRepository userRepository, 
            IRefreshTokenRepository refreshTokenRepository,
            ICryptographyService cryptoService, 
            ITokenService tokenService)
        {
            _userRepository = userRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _cryptoService = cryptoService;
            _tokenService = tokenService;
        }

        public async Task<User> RegisterAsync(string email, string password)
        {
            var existingUser = await _userRepository.GetByEmailAsync(email);
            if (existingUser != null)
                throw new Exception("Email já cadastrado.");

            var (hash, salt) = _cryptoService.HashPassword(password);

            var user = new User
            {
                Email = email,
                PasswordHash = hash,
                PasswordSalt = salt,
                Roles = new List<string> { "User" }
            };

            await _userRepository.SaveAsync(user);
            return user;
        }

        public async Task<(string AccessToken, string RefreshToken)> LoginAsync(string email, string password)
        {
            var user = await _userRepository.GetByEmailAsync(email);
            if (user == null)
                throw new UnauthorizedAccessException("Credenciais inválidas.");

            if (!_cryptoService.VerifyPassword(password, user.PasswordHash, user.PasswordSalt))
                throw new UnauthorizedAccessException("Credenciais inválidas.");

            return await GenerateTokenPairAsync(user);
        }

        public async Task<(string AccessToken, string RefreshToken)> RefreshTokenAsync(string accessToken, string refreshTokenString)
        {
            // 1. Look up Refresh Token
            var refreshToken = await _refreshTokenRepository.GetByTokenAsync(refreshTokenString);

            // 2. Not Found or Expired Check
            if (refreshToken == null || DateTime.UtcNow > DateTimeOffset.FromUnixTimeSeconds(refreshToken.ExpiresAtEpoch).UtcDateTime)
            {
                throw new UnauthorizedAccessException("Token inválido ou expirado.");
            }

            // 3. LEAK DETECTION: Reuse of Used or Invalidated Token
            if (refreshToken.IsUsed || refreshToken.IsInvalidated)
            {
                // SECURITY ALERT: Token reuse attempt!
                // Action: Invalidate ALL tokens for this user.
                await _refreshTokenRepository.RevokeAllForUserAsync(refreshToken.UserId);
                throw new UnauthorizedAccessException("Tentativa de reuso de token detectada. Acesso revogado.");
            }

            // 4. Mark Current Token as Used
            refreshToken.IsUsed = true;
            await _refreshTokenRepository.SaveAsync(refreshToken);

            // 5. Get User
            var user = await _userRepository.GetByIdAsync(refreshToken.UserId);
            if (user == null) throw new UnauthorizedAccessException("Usuário não encontrado.");

            // 6. Generate NEW Token Pair (RTR)
            return await GenerateTokenPairAsync(user);
        }

        private async Task<(string, string)> GenerateTokenPairAsync(User user)
        {
            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshTokenString = _tokenService.GenerateRefreshToken();

            var refreshTokenEntity = new RefreshToken
            {
                Token = refreshTokenString,
                UserId = user.Id,
                CreatedAt = DateTime.UtcNow,
                // TTL: 7 days
                ExpiresAtEpoch = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds(),
                IsUsed = false, 
                IsInvalidated = false
            };

            await _refreshTokenRepository.SaveAsync(refreshTokenEntity);

            return (accessToken, refreshTokenString);
        }
    }
}
