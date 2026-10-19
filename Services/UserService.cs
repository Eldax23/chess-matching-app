using System;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using ChessApp.Backend.Data;
using ChessApp.Backend.DTOs;
using ChessApp.Backend.Models;

namespace ChessApp.Backend.Services;

public interface IUserService
{
    Task<UserProfileResponse> GetProfileAsync(Guid userId);
    
    Task<UserPublicProfileResponse> GetPublicProfileAsync(Guid userId);
    
    Task<UserProfileResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);
    
    Task<string> UploadPhotoAsync(Guid userId, Stream fileStream, string contentType);
    
    Task<SetAvailabilityResponse> SetAvailabilityAsync(Guid userId, SetAvailabilityRequest request);
    
    Task<NearbyUsersResponse> FindNearbyUsersAsync(Guid userId, int radiusKm = 10, bool? hasBoard = null, int limit = 50);
    
    Task ExpireOldAvailabilityAsync();
}

public class UserService : IUserService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPhotoStorageService _photoStorageService;
    private readonly ILogger<UserService> _logger;

    public UserService(
        ApplicationDbContext dbContext,
        IPhotoStorageService photoStorageService,
        ILogger<UserService> logger)
    {
        _dbContext = dbContext;
        _photoStorageService = photoStorageService;
        _logger = logger;
    }

    public async Task<UserProfileResponse> GetProfileAsync(Guid userId)
    {
        var user = await _dbContext.Users
            .Include(u => u.Stats)
            .Include(u => u.LinkedAccounts)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            throw new KeyNotFoundException($"User {userId} not found");

        return MapToProfileResponse(user);
    }

    public async Task<UserPublicProfileResponse> GetPublicProfileAsync(Guid userId)
    {
        var user = await _dbContext.Users
            .Include(u => u.Stats)
            .Include(u => u.LinkedAccounts)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            throw new KeyNotFoundException($"User {userId} not found");

        return new UserPublicProfileResponse
        {
            Id = user.Id,
            Username = user.Username,
            FullName = user.FullName,
            PhotoUrl = user.PhotoUrl,
            Bio = user.Bio,
            HasBoard = user.HasBoard,
            FideRating = user.FideRating,
            ChessComRating = user.ChessComRating,
            LichessRating = user.LichessRating,
            LinkedAccounts = LinkedAccountMapping.ToDtos(user.LinkedAccounts),
            Stats = user.Stats != null ? MapToStatsDto(user.Stats) : null
        };
    }

    public async Task<UserProfileResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await _dbContext.Users
            .Include(u => u.Stats)
            .Include(u => u.LinkedAccounts)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            throw new KeyNotFoundException($"User {userId} not found");

        if (!string.IsNullOrEmpty(request.FullName))
            user.FullName = request.FullName;

        if (!string.IsNullOrEmpty(request.Bio))
            user.Bio = request.Bio;

        if (request.HasBoard.HasValue)
            user.HasBoard = request.HasBoard.Value;

        if (!string.IsNullOrEmpty(request.FideId))
            user.FideId = request.FideId;

        if (request.FideRating.HasValue)
        {
            user.FideRating = request.FideRating;
            user.FideRatingUpdatedAt = DateTime.UtcNow;
        }

        user.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("User profile updated: {UserId}", userId);

        return MapToProfileResponse(user);
    }

    public async Task<string> UploadPhotoAsync(Guid userId, Stream fileStream, string contentType)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
            throw new KeyNotFoundException($"User {userId} not found");

        var photoUrl = await _photoStorageService.UploadAsync(userId, fileStream, contentType);

        user.PhotoUrl = photoUrl;
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Photo uploaded for user: {UserId}", userId);

        return photoUrl;
    }

    public async Task<SetAvailabilityResponse> SetAvailabilityAsync(
        Guid userId, SetAvailabilityRequest request)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
            throw new KeyNotFoundException($"User {userId} not found");

        user.IsAvailable = request.IsAvailable;
        user.HasBoard = request.HasBoard;

        if (request.IsAvailable)
        {
            // Create Point from latitude/longitude (longitude first in PostGIS)
            var geometryFactory = new NetTopologySuite.Geometries.GeometryFactory(
                new NetTopologySuite.Geometries.PrecisionModel(), 4326);
            
            user.Location = geometryFactory.CreatePoint(
                new NetTopologySuite.Geometries.Coordinate(request.Longitude, request.Latitude));
            
            user.LastLocationUpdate = DateTime.UtcNow;
            user.AvailabilityExpiresAt = DateTime.UtcNow.AddHours(request.ExpiresInHours);
        }
        else
        {
            user.AvailabilityExpiresAt = null;
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Availability set for user {UserId}: {IsAvailable}", userId, request.IsAvailable);

        return new SetAvailabilityResponse
        {
            IsAvailable = user.IsAvailable,
            ExpiresAt = user.AvailabilityExpiresAt ?? DateTime.UtcNow,
            Location = new LocationDto
            {
                Latitude = request.Latitude,
                Longitude = request.Longitude
            }
        };
    }

    public async Task<NearbyUsersResponse> FindNearbyUsersAsync(
        Guid userId, int radiusKm = 10, bool? hasBoard = null, int limit = 50)
    {
        var currentUser = await _dbContext.Users.FindAsync(userId);
        if (currentUser == null)
            throw new KeyNotFoundException($"User {userId} not found");

        if (currentUser.Location == null)
            throw new InvalidOperationException("Current user location not set");

        // Get nearby users using PostGIS spatial query
        var radiusMeters = radiusKm * 1000.0;

        var nearbyUsers = await _dbContext.Users
            .Where(u =>
                u.Id != userId &&
                u.IsActive &&
                u.IsAvailable &&
                u.AvailabilityExpiresAt > DateTime.UtcNow &&
                u.Location != null &&
                u.Location.Distance(currentUser.Location) <= radiusMeters &&
                !_dbContext.Blocks.Any(b =>
                    (b.BlockerId == userId && b.BlockedId == u.Id) ||
                    (b.BlockerId == u.Id && b.BlockedId == userId)))
            .Include(u => u.Stats)
            .OrderBy(u => u.Location!.Distance(currentUser.Location))
            .Take(limit)
            .ToListAsync();

        var response = new NearbyUsersResponse();

        foreach (var user in nearbyUsers)
        {
            var distanceMeters = user.Location!.Distance(currentUser.Location);
            var distanceKm = Math.Round(distanceMeters / 1000.0, 2);

            if (hasBoard.HasValue && user.HasBoard != hasBoard.Value)
                continue;

            response.Users.Add(new NearbyUserDto
            {
                Id = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                PhotoUrl = user.PhotoUrl,
                Bio = user.Bio,
                FideRating = user.FideRating,
                ChessComRating = user.ChessComRating,
                LichessRating = user.LichessRating,
                HasBoard = user.HasBoard,
                DistanceKm = (decimal)distanceKm,
                Stats = user.Stats != null ? MapToStatsDto(user.Stats) : null
            });
        }

        return response;
    }

    public async Task ExpireOldAvailabilityAsync()
    {
        var expiredUsers = await _dbContext.Users
            .Where(u => u.IsAvailable && u.AvailabilityExpiresAt <= DateTime.UtcNow)
            .ToListAsync();

        foreach (var user in expiredUsers)
        {
            user.IsAvailable = false;
            user.AvailabilityExpiresAt = null;
            user.UpdatedAt = DateTime.UtcNow;
        }

        if (expiredUsers.Any())
        {
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Expired availability for {Count} users", expiredUsers.Count);
        }
    }

    private UserProfileResponse MapToProfileResponse(User user)
    {
        return new UserProfileResponse
        {
            Id = user.Id,
            Email = user.Email,
            Username = user.Username,
            FullName = user.FullName,
            PhotoUrl = user.PhotoUrl,
            Bio = user.Bio,
            IsAvailable = user.IsAvailable,
            AvailabilityExpiresAt = user.AvailabilityExpiresAt,
            HasBoard = user.HasBoard,
            FideId = user.FideId,
            FideRating = user.FideRating,
            ChessCom = user.ChessComUsername != null ? new ChessRatingDto
            {
                Username = user.ChessComUsername,
                Url = user.ChessComUrl,
                Rating = user.ChessComRating
            } : null,
            Lichess = user.LichessUsername != null ? new ChessRatingDto
            {
                Username = user.LichessUsername,
                Url = user.LichessUrl,
                Rating = user.LichessRating
            } : null,
            LinkedAccounts = LinkedAccountMapping.ToDtos(user.LinkedAccounts),
            Stats = user.Stats != null ? MapToStatsDto(user.Stats) : null,
            CreatedAt = user.CreatedAt,
            LastLogin = user.LastLogin
        };
    }

    private UserStatsDto MapToStatsDto(UserStats stats)
    {
        return new UserStatsDto
        {
            TotalMatchesPlayed = stats.TotalMatchesPlayed,
            TotalWins = stats.TotalWins,
            TotalLosses = stats.TotalLosses,
            TotalDraws = stats.TotalDraws,
            WinRate = stats.WinRate,
            AvgOpponentRating = stats.AvgOpponentRating
        };
    }
}
