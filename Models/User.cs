using NetTopologySuite.Geometries;

namespace ChessApp.Backend.Models;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string? FullName { get; set; }

    // Profile
    public string? PhotoUrl { get; set; }
    public string? Bio { get; set; }

    // Location (PostGIS Point)
    public Point? Location { get; set; }
    public DateTime? LastLocationUpdate { get; set; }

    // Availability
    public bool IsAvailable { get; set; } = false;
    public DateTime? AvailabilityExpiresAt { get; set; }
    public bool HasBoard { get; set; } = false;

    // Chess credentials - FIDE
    public string? FideId { get; set; }
    public int? FideRating { get; set; }
    public DateTime? FideRatingUpdatedAt { get; set; }

    // Chess.com - denormalized from the (unverified) LinkedAccount, set only
    // by ChessComIntegrationService
    public string? ChessComUsername { get; set; }
    public string? ChessComUrl { get; set; }
    public int? ChessComRating { get; set; }

    // Lichess - denormalized from the verified LinkedAccount (set only by
    // LichessIntegrationService, never from user input)
    public string? LichessUsername { get; set; }
    public string? LichessUrl { get; set; }
    public int? LichessRating { get; set; }

    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLogin { get; set; }
    public bool IsActive { get; set; } = true;

    public List<string> DeviceTokens { get; set; } = new();

    // Navigation
    public virtual List<Proposal> ProposalsSent { get; set; } = new();
    public virtual List<Proposal> ProposalsReceived { get; set; } = new();
    public virtual List<Match> MatchesAsPlayer1 { get; set; } = new();
    public virtual List<Match> MatchesAsPlayer2 { get; set; } = new();
    public virtual UserStats? Stats { get; set; }
    public virtual List<Block> BlockedBy { get; set; } = new();
    public virtual List<Block> Blocking { get; set; } = new();
    public virtual List<RefreshToken> RefreshTokens { get; set; } = new();
    public virtual List<RatingSnapshot> RatingSnapshots { get; set; } = new();
    public virtual List<LinkedAccount> LinkedAccounts { get; set; } = new();
    public virtual List<Review> ReviewsGiven { get; set; } = new();
    public virtual List<Review> ReviewsReceived { get; set; } = new();
}
