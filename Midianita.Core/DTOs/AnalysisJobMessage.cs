namespace Midianita.Core.DTOs
{
    /// <summary>
    /// Message published to the SQS Analysis Queue.
    /// Following the Claim Check pattern: only the BannerId is sent; the Lambda
    /// retrieves the full image and metadata from S3/DynamoDB using this ID.
    /// </summary>
    public class AnalysisJobMessage
    {
        public Guid BannerId { get; set; }
    }
}
