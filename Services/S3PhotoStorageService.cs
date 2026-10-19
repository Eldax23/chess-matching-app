using Amazon.S3;
using Amazon.S3.Model;

namespace ChessApp.Backend.Services;

// Production storage: any S3-compatible provider (Cloudflare R2, Backblaze
// B2, AWS S3, ...) configured via Storage:S3 in appsettings. R2 is the
// intended default - no egress fees and a real (non-trial) free tier.
public class S3PhotoStorageService : IPhotoStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _publicBaseUrl;
    private readonly ILogger<S3PhotoStorageService> _logger;

    public S3PhotoStorageService(IAmazonS3 s3Client, IConfiguration configuration, ILogger<S3PhotoStorageService> logger)
    {
        _s3Client = s3Client;
        _logger = logger;

        // Presence is checked at startup in Program.cs
        _bucketName = configuration["Storage:S3:BucketName"]!;
        _publicBaseUrl = configuration["Storage:S3:PublicBaseUrl"]!.TrimEnd('/');
    }

    public async Task<string> UploadAsync(Guid userId, Stream fileStream, string contentType)
    {
        var key = PhotoKeys.For(userId, contentType);

        try
        {
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = fileStream,
                ContentType = contentType,
                AutoCloseStream = false,
                // R2 doesn't support streaming SigV4 payload signing
                DisablePayloadSigning = true
            };

            await _s3Client.PutObjectAsync(request);

            _logger.LogInformation("Photo uploaded to object storage for user {UserId}: {Key}", userId, key);

            return $"{_publicBaseUrl}/{key}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload photo to object storage for user {UserId}", userId);
            throw;
        }
    }
}
