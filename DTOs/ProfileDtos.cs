namespace ChessApp.Backend.DTOs;

public class UpdateProfileRequest
{
    public string? FullName { get; set; }
    public string? Bio { get; set; }
    public bool? HasBoard { get; set; }
    public string? FideId { get; set; }
    public int? FideRating { get; set; }
    // Lichess and chess.com are linked via IntegrationsController
}

public class ChessRatingDto
{
    public string? Username { get; set; }
    public string? Url { get; set; }
    public int? Rating { get; set; }
}

public class UserStatsDto
{
    public int TotalMatchesPlayed { get; set; }
    public int TotalWins { get; set; }
    public int TotalLosses { get; set; }
    public int TotalDraws { get; set; }
    public int WinRate { get; set; }
    public int? AvgOpponentRating { get; set; }
}

public class UserProfileResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string? FullName { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Bio { get; set; }

    public bool IsAvailable { get; set; }
    public DateTime? AvailabilityExpiresAt { get; set; }
    public bool HasBoard { get; set; }

    public string? FideId { get; set; }
    public int? FideRating { get; set; }

    public ChessRatingDto? ChessCom { get; set; }
    public ChessRatingDto? Lichess { get; set; }

    // Verified platform accounts with per-time-control ratings
    public List<LinkedAccountDto> LinkedAccounts { get; set; } = new();

    public UserStatsDto? Stats { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
}

public class UserPublicProfileResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = null!;
    public string? FullName { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Bio { get; set; }

    public bool HasBoard { get; set; }
    public int? FideRating { get; set; }
    public int? ChessComRating { get; set; }
    public int? LichessRating { get; set; }

    public List<LinkedAccountDto> LinkedAccounts { get; set; } = new();

    public UserStatsDto? Stats { get; set; }
}
