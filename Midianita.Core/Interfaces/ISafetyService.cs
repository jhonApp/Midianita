namespace Midianita.Core.Interfaces
{
    /// <summary>
    /// Core/Application Layer: Interface for safety checking service
    /// </summary>
    public interface ISafetyService
    {
        bool IsContentSafe(string content);
    }
}
