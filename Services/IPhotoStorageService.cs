namespace ChessApp.Backend.Services;

public interface IPhotoStorageService
{
    Task<string> UploadAsync(Guid userId, Stream fileStream, string fileName);
}
