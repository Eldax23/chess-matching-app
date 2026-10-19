namespace ChessApp.Backend.Services;

// Development-only storage: saves to a local folder (served back out via
// app.UseStaticFiles in Program.cs) so photo upload works with zero signup,
// zero cost, and no emulator. Production uses S3PhotoStorageService instead.
public class LocalPhotoStorageService : IPhotoStorageService
{
    private readonly string _uploadsRoot;
    private readonly string _publicBaseUrl;
    private readonly ILogger<LocalPhotoStorageService> _logger;

    public LocalPhotoStorageService(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<LocalPhotoStorageService> logger)
    {
        _logger = logger;

        var relativePath = configuration["Storage:LocalUploadsPath"] ?? "uploads";
        _uploadsRoot = Path.Combine(environment.ContentRootPath, relativePath);
        Directory.CreateDirectory(_uploadsRoot);

        _publicBaseUrl = (configuration["Storage:PublicBaseUrl"] ?? "http://localhost:5000").TrimEnd('/');
    }

    public async Task<string> UploadAsync(Guid userId, Stream fileStream, string contentType)
    {
        var key = PhotoKeys.For(userId, contentType);
        var absolutePath = Path.Combine(_uploadsRoot, key);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        using (var output = new FileStream(absolutePath, FileMode.Create))
        {
            await fileStream.CopyToAsync(output);
        }

        _logger.LogInformation("Photo saved locally for user {UserId} at {Path}", userId, absolutePath);

        return $"{_publicBaseUrl}/uploads/{key}";
    }
}
