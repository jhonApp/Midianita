using Konscious.Security.Cryptography;
using Midianita.Core.Interfaces;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Midianita.Infrastructure.Services
{
    public class Argon2PasswordHasher : ICryptographyService
    {
        // Security Constraints as requested:
        // Memory: 40MB (40 * 1024)
        // Iterations: 4
        // Parallelism: 4
        
        private const int MemorySize = 40 * 1024;
        private const int Iterations = 4;
        private const int Parallelism = 4;
        private const int SaltSize = 16;
        private const int HashSize = 32;

        public (string Hash, byte[] Salt) HashPassword(string password)
        {
            var salt = CreateSalt();
            var hash = HashPasswordWithSalt(password, salt);
            return (Convert.ToBase64String(hash), salt);
        }

        public bool VerifyPassword(string password, string hash, byte[] salt)
        {
            var newHash = HashPasswordWithSalt(password, salt);
            var oldHash = Convert.FromBase64String(hash);
            
            return newHash.SequenceEqual(oldHash);
        }

        private byte[] CreateSalt()
        {
            var buffer = new byte[SaltSize];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(buffer);
            return buffer;
        }

        private byte[] HashPasswordWithSalt(string password, byte[] salt)
        {
            using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password));
            argon2.Salt = salt;
            argon2.DegreeOfParallelism = Parallelism;
            argon2.Iterations = Iterations;
            argon2.MemorySize = MemorySize;

            return argon2.GetBytes(HashSize);
        }
    }
}
