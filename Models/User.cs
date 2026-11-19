using NetTopologySuite.Geometries;

namespace ChessApp.Backend.Models;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string? FullName { get; set; }

    // Email verification. Null until the player enters the code we emailed
    // them (or signs up with Lichess, which already confirmed the email).
    public DateTime? EmailVerifiedAt { get; set; }
    // When the first code was sent; unverified accounts are deleted a week later
    public DateTime? EmailVerificationRequestedAt { get; set; }
    public string? EmailVerificationCodeHash { get; set; }
    public DateTime? EmailVerificationCodeExpiresAt { get; set; }
    public DateTime? EmailVerificationCodeSentAt { get; set; }
    public int EmailVerificationFailedAttempts { get; set; }

    public bool IsEmailVerified => EmailVerifiedAt != null;

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
    public virtual List<Block> BlockedBy { get; set; } = new();
    public virtual List<Block> Blocking { get; set; } = new();
    public virtual List<RefreshToken> RefreshTokens { get; set; } = new();
    public virtual List<RatingSnapshot> RatingSnapshots { get; set; } = new();
    public virtual List<LinkedAccount> LinkedAccounts { get; set; } = new();
    public virtual List<Review> ReviewsGiven { get; set; } = new();
    public virtual List<Review> ReviewsReceived { get; set; } = new();
}
