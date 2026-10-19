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

        _bucketName = configuration["Storage:S3:BucketName"]
            ?? throw new InvalidOperationException("Storage:S3:BucketName not configured");

        _publicBaseUrl = (configuration["Storage:S3:PublicBaseUrl"]
            ?? throw new InvalidOperationException("Storage:S3:PublicBaseUrl not configured")).TrimEnd('/');
    }

    public async Task<string> UploadAsync(Guid userId, Stream fileStream, string fileName)
    {
        var key = $"profiles/{userId}/{Guid.NewGuid()}-{fileName}";

        try
        {
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = fileStream,
                AutoCloseStream = false
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
