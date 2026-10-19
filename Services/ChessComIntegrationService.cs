using System.Net;
using Microsoft.EntityFrameworkCore;
using ChessApp.Backend.Data;
using ChessApp.Backend.DTOs;
using ChessApp.Backend.Models;

namespace ChessApp.Backend.Services;

public interface IChessComIntegrationService
{
    Task<LinkedAccountDto> LinkAsync(Guid userId, string username, CancellationToken ct = default);
    Task<LinkedAccountDto> RefreshAsync(Guid userId, CancellationToken ct = default);
    Task UnlinkAsync(Guid userId, CancellationToken ct = default);
    Task<int> SyncStaleAccountsAsync(TimeSpan maxAge, CancellationToken ct = default);
}

/// <summary>
/// chess.com account linking by username, from the public API. Ownership is
/// NOT verified (chess.com OAuth pending), so these links are stored with
/// IsVerified = false and several users may claim the same account.
/// </summary>
public class ChessComIntegrationService : IChessComIntegrationService
{
    // chess.com has no batch endpoint and asks for serial requests, so each
    // sync tick refreshes at most this many accounts (2 requests each)
    private const int MaxAccountsPerSync = 50;

    private static readonly TimeSpan ManualRefreshCooldown = TimeSpan.FromMinutes(5);

    // Live time controls considered for the headline rating (User.ChessComRating)
    private static readonly string[] HeadlineCategories = { "rapid", "blitz", "bullet" };

    private readonly ApplicationDbContext _dbContext;
    private readonly IChessComClient _chessCom;
    private readonly ILogger<ChessComIntegrationService> _logger;

    public ChessComIntegrationService(
        ApplicationDbContext dbContext,
        IChessComClient chessCom,
        ILogger<ChessComIntegrationService> logger)
    {
        _dbContext = dbContext;
        _chessCom = chessCom;
        _logger = logger;
    }

    public async Task<LinkedAccountDto> LinkAsync(Guid userId, string username, CancellationToken ct = default)
    {
        username = username.Trim();

        var player = await _chessCom.GetPlayerAsync(username, ct)
            ?? throw new KeyNotFoundException($"No chess.com player named '{username}'.");
        if (player.Status?.StartsWith("closed") == true)
            throw new InvalidOperationException($"The chess.com account '{username}' is closed.");

        var stats = await _chessCom.GetStatsAsync(player.Username, ct);

        var user = await _dbContext.Users
            .Include(u => u.LinkedAccounts)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException($"User {userId} not found");

        var account = user.LinkedAccounts.FirstOrDefault(a => a.Platform == LinkedPlatforms.ChessCom);
        if (account == null)
        {
            account = new LinkedAccount
            {
                UserId = userId,
                Platform = LinkedPlatforms.ChessCom,
                IsVerified = false
            };
            _dbContext.LinkedAccounts.Add(account);
        }
        account.ConnectedAt = DateTime.UtcNow;

        Apply(account, user, player, stats);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("User {UserId} linked chess.com account {Username} (unverified)", userId, player.Username);
        return LinkedAccountMapping.ToDto(account);
    }

    public async Task<LinkedAccountDto> RefreshAsync(Guid userId, CancellationToken ct = default)
    {
        var account = await _dbContext.LinkedAccounts
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Platform == LinkedPlatforms.ChessCom, ct)
            ?? throw new KeyNotFoundException("No chess.com account linked");

        if (DateTime.UtcNow - account.LastSyncedAt < ManualRefreshCooldown)
            return LinkedAccountMapping.ToDto(account);

        if (!await SyncAsync(account, ct))
            throw new KeyNotFoundException("The linked chess.com account no longer exists");

        await _dbContext.SaveChangesAsync(ct);
        return LinkedAccountMapping.ToDto(account);
    }

    public async Task UnlinkAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _dbContext.Users
            .Include(u => u.LinkedAccounts)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException($"User {userId} not found");

        var account = user.LinkedAccounts.FirstOrDefault(a => a.Platform == LinkedPlatforms.ChessCom);
        if (account != null)
            _dbContext.LinkedAccounts.Remove(account);

        user.ChessComUsername = null;
        user.ChessComUrl = null;
        user.ChessComRating = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("User {UserId} unlinked chess.com", userId);
    }

    public async Task<int> SyncStaleAccountsAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var accounts = await _dbContext.LinkedAccounts
            .Include(a => a.User)
            .Where(a => a.Platform == LinkedPlatforms.ChessCom && a.LastSyncedAt < cutoff)
            .OrderBy(a => a.LastSyncedAt)
            .Take(MaxAccountsPerSync)
            .ToListAsync(ct);

        var synced = 0;
        foreach (var account in accounts)
        {
            try
            {
                // Renamed/closed accounts 404: keep the last snapshot
                if (!await SyncAsync(account, ct))
                    account.LastSyncedAt = DateTime.UtcNow;
                synced++;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("chess.com rate limit hit, resuming next tick");
                break;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "chess.com sync failed for {Username}", account.Username);
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        return synced;
    }

    // Re-fetches one account; false when chess.com no longer knows the username
    private async Task<bool> SyncAsync(LinkedAccount account, CancellationToken ct)
    {
        var player = await _chessCom.GetPlayerAsync(account.Username, ct);
        if (player == null)
            return false;

        var stats = await _chessCom.GetStatsAsync(player.Username, ct);
        Apply(account, account.User, player, stats);
        return true;
    }

    private void Apply(LinkedAccount account, User user, ChessComPlayer player, ChessComStats stats)
    {
        var now = DateTime.UtcNow;

        var categories = new (string Category, ChessComCategory? Data)[]
        {
            ("bullet", stats.Bullet),
            ("blitz", stats.Blitz),
            ("rapid", stats.Rapid),
            ("daily", stats.Daily),
            ("chess960", stats.Chess960Daily),
        };

        account.ExternalId = player.PlayerId.ToString();
        account.Username = DisplayUsername(player);
        account.ProfileUrl = player.Url ?? $"https://www.chess.com/member/{player.Username}";
        account.Title = player.Title;
        account.Ratings = categories
            .Where(c => c.Data?.Last != null)
            .Select(c => new PlatformRating
            {
                Category = c.Category,
                Rating = c.Data!.Last!.Rating,
                Best = c.Data.Best?.Rating,
                Games = (c.Data.Record?.Win ?? 0) + (c.Data.Record?.Loss ?? 0) + (c.Data.Record?.Draw ?? 0),
                // chess.com exposes no provisional flag or recent progress
                Provisional = false,
            })
            .ToList();
        account.Wins = categories.Sum(c => c.Data?.Record?.Win ?? 0);
        account.Losses = categories.Sum(c => c.Data?.Record?.Loss ?? 0);
        account.Draws = categories.Sum(c => c.Data?.Record?.Draw ?? 0);
        account.GamesPlayed = account.Wins + account.Losses + account.Draws;
        account.PlatformJoinedAt = FromUnixSeconds(player.Joined);
        account.PlatformLastSeenAt = FromUnixSeconds(player.LastOnline);
        account.LastSyncedAt = now;

        // Keep the denormalized User columns in sync for list responses
        var headline = account.Ratings
            .Where(r => HeadlineCategories.Contains(r.Category))
            .MaxBy(r => r.Games)?.Rating;
        if (headline.HasValue && headline != user.ChessComRating)
        {
            _dbContext.RatingSnapshots.Add(new RatingSnapshot
            {
                UserId = user.Id,
                Platform = LinkedPlatforms.ChessCom,
                Rating = headline.Value,
                CapturedAt = now
            });
        }
        user.ChessComUsername = account.Username;
        user.ChessComUrl = account.ProfileUrl;
        user.ChessComRating = headline;
        user.UpdatedAt = now;
    }

    // The API's username is lowercase; the profile URL keeps the user's casing
    private static string DisplayUsername(ChessComPlayer player)
    {
        var fromUrl = player.Url?.TrimEnd('/').Split('/').LastOrDefault();
        return string.Equals(fromUrl, player.Username, StringComparison.OrdinalIgnoreCase)
            ? fromUrl!
            : player.Username;
    }

    private static DateTime? FromUnixSeconds(long? s) =>
        s.HasValue ? DateTimeOffset.FromUnixTimeSeconds(s.Value).UtcDateTime : null;
}
