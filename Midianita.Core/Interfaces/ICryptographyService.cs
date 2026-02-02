namespace Midianita.Core.Interfaces
{
    public interface ICryptographyService
    {
        (string Hash, byte[] Salt) HashPassword(string password);
        bool VerifyPassword(string password, string hash, byte[] salt);
    }
}
