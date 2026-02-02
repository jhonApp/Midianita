using FluentAssertions;
using Midianita.Aplication.Service;
using Midianita.Core.Entities;
using Midianita.Core.Interfaces;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Midianita.Test
{
    public class AuthServiceTests
    {
        private readonly Mock<IUserRepository> _userRepoMock;
        private readonly Mock<IRefreshTokenRepository> _refreshTokenRepoMock;
        private readonly Mock<ICryptographyService> _cryptoMock;
        private readonly Mock<ITokenService> _tokenServiceMock;
        private readonly AuthService _authService;

        public AuthServiceTests()
        {
            _userRepoMock = new Mock<IUserRepository>();
            _refreshTokenRepoMock = new Mock<IRefreshTokenRepository>();
            _cryptoMock = new Mock<ICryptographyService>();
            _tokenServiceMock = new Mock<ITokenService>();

            _authService = new AuthService(
                _userRepoMock.Object,
                _refreshTokenRepoMock.Object,
                _cryptoMock.Object,
                _tokenServiceMock.Object
            );
        }

        [Fact]
        public async Task RegisterAsync_ShouldCreateUser_WhenEmailIsUnique()
        {
            // Arrange
            var email = "test@example.com";
            var password = "password123";
            _userRepoMock.Setup(x => x.GetByEmailAsync(email)).ReturnsAsync((User?)null);
            _cryptoMock.Setup(x => x.HashPassword(password)).Returns(("hashed", new byte[0]));

            // Act
            var result = await _authService.RegisterAsync(email, password);

            // Assert
            result.Should().NotBeNull();
            result.Email.Should().Be(email);
            result.PasswordHash.Should().Be("hashed");
            _userRepoMock.Verify(x => x.SaveAsync(It.Is<User>(u => u.Email == email)), Times.Once);
        }

        [Fact]
        public async Task RegisterAsync_ShouldThrow_WhenEmailExists()
        {
            // Arrange
            var email = "existing@example.com";
            _userRepoMock.Setup(x => x.GetByEmailAsync(email)).ReturnsAsync(new User { Email = email });

            // Act
            Func<Task> act = async () => await _authService.RegisterAsync(email, "pass");

            // Assert
            await act.Should().ThrowAsync<Exception>().WithMessage("Email já cadastrado.");
        }

        [Fact]
        public async Task LoginAsync_ShouldReturnTokens_WhenCredentialsAreValid()
        {
            // Arrange
            var email = "user@example.com";
            var password = "password";
            var user = new User { Id = "1", Email = email, PasswordHash = "hash" };

            _userRepoMock.Setup(x => x.GetByEmailAsync(email)).ReturnsAsync(user);
            _cryptoMock.Setup(x => x.VerifyPassword(password, "hash", It.IsAny<byte[]>())).Returns(true);
            _tokenServiceMock.Setup(x => x.GenerateAccessToken(user)).Returns("access_token");
            _tokenServiceMock.Setup(x => x.GenerateRefreshToken()).Returns("refresh_token_string");

            // Act
            var result = await _authService.LoginAsync(email, password);

            // Assert
            result.AccessToken.Should().Be("access_token");
            result.RefreshToken.Should().Be("refresh_token_string");
            _refreshTokenRepoMock.Verify(x => x.SaveAsync(It.IsAny<RefreshToken>()), Times.Once);
        }

        [Fact]
        public async Task RefreshTokenAsync_ShouldRotateTokens_WhenTokenIsValid()
        {
            // Arrange
            var tokenString = "valid_token";
            var userId = "user1";
            var oldToken = new RefreshToken 
            { 
                Token = tokenString, 
                UserId = userId, 
                ExpiresAtEpoch = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
                IsUsed = false,
                IsInvalidated = false
            };
            var user = new User { Id = userId };

            _refreshTokenRepoMock.Setup(x => x.GetByTokenAsync(tokenString)).ReturnsAsync(oldToken);
            _userRepoMock.Setup(x => x.GetByIdAsync(userId)).ReturnsAsync(user);
            _tokenServiceMock.Setup(x => x.GenerateAccessToken(user)).Returns("new_access");
            _tokenServiceMock.Setup(x => x.GenerateRefreshToken()).Returns("new_refresh");

            // Act
            var result = await _authService.RefreshTokenAsync("old_access", tokenString);

            // Assert
            result.AccessToken.Should().Be("new_access");
            result.RefreshToken.Should().Be("new_refresh");

            // Verify Rotation: Old token marked used
            oldToken.IsUsed.Should().BeTrue();
            _refreshTokenRepoMock.Verify(x => x.SaveAsync(oldToken), Times.Once);
            
            // Verify New Token Saved
            _refreshTokenRepoMock.Verify(x => x.SaveAsync(It.Is<RefreshToken>(rt => rt.Token == "new_refresh")), Times.Once);
        }

        [Fact]
        public async Task RefreshTokenAsync_ShouldDetectLeakAndRevoke_WhenTokenIsReused()
        {
            // Arrange
            var tokenString = "stolen_used_token";
            var userId = "victim_user";
            var usedToken = new RefreshToken
            {
                Token = tokenString,
                UserId = userId,
                ExpiresAtEpoch = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
                IsUsed = true, // ALREADY USED
                IsInvalidated = false
            };

            _refreshTokenRepoMock.Setup(x => x.GetByTokenAsync(tokenString)).ReturnsAsync(usedToken);

            // Act
            Func<Task> act = async () => await _authService.RefreshTokenAsync("access", tokenString);

            // Assert
            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("*reuso de token detectada*");

            // Verify Security Measure: ALL tokens revoked
            _refreshTokenRepoMock.Verify(x => x.RevokeAllForUserAsync(userId), Times.Once);
        }
    }
}
