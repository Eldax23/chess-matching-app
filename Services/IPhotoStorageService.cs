namespace ChessApp.Backend.Services;

public interface IPhotoStorageService
{
    Task<string> UploadAsync(Guid userId, Stream fileStream, string contentType);
}

public static class PhotoKeys
{
    // Content types ProfileController.UploadPhoto accepts
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
    };

    // The client's file name is never used: it can contain spaces, '#' or
    // path segments that would break the public URL
    public static string For(Guid userId, string contentType) =>
        $"profiles/{userId}/{Guid.NewGuid()}{Extensions.GetValueOrDefault(contentType, "")}";
}
