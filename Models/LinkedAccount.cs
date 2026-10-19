namespace ChessApp.Backend.Models;

/// <summary>
/// An external chess platform account linked to a user. Lichess ownership is
/// proven via OAuth; chess.com is a self-declared username (IsVerified = false)
/// until chess.com OAuth is available. Ratings are a public snapshot refreshed
/// by LinkedAccountSyncWorker; no platform access token is stored.
/// </summary>
public class LinkedAccount
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public virtual User User { get; set; } = null!;

    public string Platform { get; set; } = null!; // see LinkedPlatforms

    // Stable platform identifier (Lichess: lowercase user id, chess.com: player_id)
    public string ExternalId { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string ProfileUrl { get; set; } = null!;
    public string? Title { get; set; } // GM, IM, FM, ...

    // True only when ownership was proven (OAuth)
    public bool IsVerified { get; set; }

    public List<PlatformRating> Ratings { get; set; } = new();

    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }

    public DateTime? PlatformJoinedAt { get; set; }
    public DateTime? PlatformLastSeenAt { get; set; }

    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Rating in one time control, stored as jsonb on LinkedAccount.</summary>
public class PlatformRating
{
    public string Category { get; set; } = null!; // bullet, blitz, rapid, classical, ...
    public int Rating { get; set; }
    public int Games { get; set; }
    public int? Best { get; set; } // peak rating, when the platform reports it
    public bool Provisional { get; set; }
    public int? Progress { get; set; } // rating change over the last games
}

public static class LinkedPlatforms
{
    public const string Lichess = "lichess";
    public const string ChessCom = "chesscom";
}
