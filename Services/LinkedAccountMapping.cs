using ChessApp.Backend.DTOs;
using ChessApp.Backend.Models;

namespace ChessApp.Backend.Services;

internal static class LinkedAccountMapping
{
    public static LinkedAccountDto ToDto(LinkedAccount account) => new()
    {
        Platform = account.Platform,
        Username = account.Username,
        ProfileUrl = account.ProfileUrl,
        Title = account.Title,
        Verified = account.IsVerified,
        Ratings = account.Ratings.Select(r => new PlatformRatingDto
        {
            Category = r.Category,
            Rating = r.Rating,
            Best = r.Best,
            Games = r.Games,
            Provisional = r.Provisional,
            Progress = r.Progress
        }).ToList(),
        GamesPlayed = account.GamesPlayed,
        Wins = account.Wins,
        Losses = account.Losses,
        Draws = account.Draws,
        MemberSince = account.PlatformJoinedAt,
        LastSeenAt = account.PlatformLastSeenAt,
        ConnectedAt = account.ConnectedAt,
        LastSyncedAt = account.LastSyncedAt
    };

    public static List<LinkedAccountDto> ToDtos(IEnumerable<LinkedAccount> accounts) =>
        accounts.OrderBy(a => a.Platform).Select(ToDto).ToList();
}
