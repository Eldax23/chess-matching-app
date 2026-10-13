using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using ChessApp.Backend.Models;

namespace ChessApp.Backend.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // DbSets
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Proposal> Proposals { get; set; } = null!;
    public DbSet<Match> Matches { get; set; } = null!;
    public DbSet<UserStats> UserStats { get; set; } = null!;
    public DbSet<Block> Blocks { get; set; } = null!;
    public DbSet<RatingSnapshot> RatingSnapshots { get; set; } = null!;
    public DbSet<LinkedAccount> LinkedAccounts { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<Review> Reviews { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure geospatial support
        NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

        // =====================================================
        // USERS
        // =====================================================

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.IsAvailable)
                .HasFilter("is_available = true");
            entity.HasIndex(e => e.AvailabilityExpiresAt)
                .HasFilter("is_available = true");

            // Spatial index for location queries
            entity.HasIndex(e => e.Location)
                .HasMethod("GIST");

            // Property configs
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Username).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(255).IsRequired();
            entity.Property(e => e.FullName).HasMaxLength(255);
            entity.Property(e => e.PhotoUrl).HasMaxLength(500);
            entity.Property(e => e.Bio).HasColumnType("text");

            // PostGIS Point for location
            entity.Property(e => e.Location)
                .HasColumnType("geography (point, 4326)");

            entity.Property(e => e.FideId).HasMaxLength(20);
            entity.Property(e => e.ChessComUsername).HasMaxLength(100);
            entity.Property(e => e.ChessComUrl).HasMaxLength(255);
            entity.Property(e => e.LichessUsername).HasMaxLength(100);
            entity.Property(e => e.LichessUrl).HasMaxLength(255);

            // Device tokens array
            entity.Property(e => e.DeviceTokens)
                .HasConversion(
                    v => string.Join(",", v),
                    v => v.Split(",", StringSplitOptions.RemoveEmptyEntries).ToList());

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("NOW()");

            // Relationships
            entity.HasMany(e => e.ProposalsSent)
                .WithOne(p => p.Proposer)
                .HasForeignKey(p => p.ProposerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.ProposalsReceived)
                .WithOne(p => p.Receiver)
                .HasForeignKey(p => p.ReceiverId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.MatchesAsPlayer1)
                .WithOne(m => m.Player1)
                .HasForeignKey(m => m.Player1Id)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.MatchesAsPlayer2)
                .WithOne(m => m.Player2)
                .HasForeignKey(m => m.Player2Id)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Stats)
                .WithOne(s => s.User)
                .HasForeignKey<UserStats>(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.BlockedBy)
                .WithOne(b => b.Blocked)
                .HasForeignKey(b => b.BlockedId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Blocking)
                .WithOne(b => b.Blocker)
                .HasForeignKey(b => b.BlockerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.ReviewsReceived)
                .WithOne(r => r.Reviewee)
                .HasForeignKey(r => r.RevieweeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.ReviewsGiven)
                .WithOne(r => r.Reviewer)
                .HasForeignKey(r => r.ReviewerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // =====================================================
        // PROPOSALS
        // =====================================================

        modelBuilder.Entity<Proposal>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.ReceiverId, e.Status })
                .HasFilter("status = 'pending'");

            entity.HasIndex(e => e.ProposerId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ExpiresAt);

            // Spatial index for meeting location queries
            entity.HasIndex(e => e.MeetingLocation)
                .HasMethod("GIST");

            entity.Property(e => e.Status)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<ProposalStatus>(v, ignoreCase: true));

            entity.Property(e => e.Message).HasColumnType("text");

            // PostGIS Point for meeting location
            entity.Property(e => e.MeetingLocation)
                .HasColumnType("geography (point, 4326)");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");

            // Constraint: proposer != receiver
            entity.ToTable(t => t.HasCheckConstraint("ck_different_users", "proposer_id != receiver_id"));

            // Optional match relationship
            entity.HasOne(e => e.Match)
                .WithOne(m => m.Proposal)
                .HasForeignKey<Proposal>(p => p.MatchId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // =====================================================
        // MATCHES
        // =====================================================

        modelBuilder.Entity<Match>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.Player1Id);
            entity.HasIndex(e => e.Player2Id);
            entity.HasIndex(e => e.PlayedAt);

            entity.Property(e => e.Outcome)
                .HasConversion(
                    v => MatchOutcomeToString(v),
                    v => MatchOutcomeFromString(v));

            entity.Property(e => e.Location)
                .HasColumnType("geography (point, 4326)");

            entity.Property(e => e.TimeControl).HasMaxLength(50);
            entity.Property(e => e.Pgn).HasColumnType("text");
            entity.Property(e => e.Notes).HasColumnType("text");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("NOW()");
        });

        // =====================================================
        // USER STATS
        // =====================================================

        modelBuilder.Entity<UserStats>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.UserId);

            entity.Property(e => e.LastStatsUpdate)
                .HasDefaultValueSql("NOW()");
        });

        // =====================================================
        // BLOCKS
        // =====================================================

        modelBuilder.Entity<Block>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.BlockerId);
            entity.HasIndex(e => e.BlockedId);

            // Unique constraint: one block per blocker-blocked pair
            entity.HasIndex(e => new { e.BlockerId, e.BlockedId })
                .IsUnique();

            entity.Property(e => e.Reason).HasColumnType("text");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");

            // Constraint: blocker != blocked
            entity.ToTable(t => t.HasCheckConstraint("ck_different_users", "blocker_id != blocked_id"));
        });

        // =====================================================
        // REVIEWS
        // =====================================================

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.RevieweeId);
            entity.HasIndex(e => e.ReviewerId);

            // Unique constraint: one review per reviewer per match
            entity.HasIndex(e => new { e.MatchId, e.ReviewerId })
                .IsUnique();

            entity.Property(e => e.Comment).HasColumnType("text");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.Match)
                .WithMany(m => m.Reviews)
                .HasForeignKey(e => e.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            // Constraint: reviewer != reviewee, rating within 1-5
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_different_users", "reviewer_id != reviewee_id");
                t.HasCheckConstraint("ck_rating_range", "rating >= 1 AND rating <= 5");
            });
        });

        // =====================================================
        // RATING SNAPSHOTS
        // =====================================================

        modelBuilder.Entity<RatingSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.UserId, e.Platform });

            entity.Property(e => e.Platform).HasMaxLength(50).IsRequired();

            entity.Property(e => e.CapturedAt)
                .HasDefaultValueSql("NOW()");
        });

        // =====================================================
        // LINKED ACCOUNTS
        // =====================================================

        modelBuilder.Entity<LinkedAccount>(entity =>
        {
            entity.HasKey(e => e.Id);

            // One account per platform per user, and a verified external
            // account can only be claimed by one user. Unverified claims
            // (chess.com usernames) may overlap, so a squatter can't block
            // the real owner.
            entity.HasIndex(e => new { e.UserId, e.Platform }).IsUnique();
            entity.HasIndex(e => new { e.Platform, e.ExternalId })
                .IsUnique()
                .HasFilter("is_verified = true");
            entity.HasIndex(e => e.LastSyncedAt);

            entity.Property(e => e.Platform).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ExternalId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Username).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ProfileUrl).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(10);

            entity.Property(e => e.Ratings)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<PlatformRating>>(v, (JsonSerializerOptions?)null) ?? new(),
                    new ValueComparer<List<PlatformRating>>(
                        (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) ==
                                  JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                        v => JsonSerializer.Deserialize<List<PlatformRating>>(
                            JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                            (JsonSerializerOptions?)null)!));

            entity.HasOne(e => e.User)
                .WithMany(u => u.LinkedAccounts)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // =====================================================
        // REFRESH TOKENS
        // =====================================================

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ExpiresAt);

            entity.Property(e => e.TokenHash)
                .HasMaxLength(255)
                .IsRequired();

            entity.HasIndex(e => e.TokenHash)
                .IsUnique();

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");
        });
    }

    private static string MatchOutcomeToString(MatchOutcome outcome) => outcome switch
    {
        MatchOutcome.NotPlayed => "not_played",
        MatchOutcome.Player1Won => "player1_won",
        MatchOutcome.Player2Won => "player2_won",
        MatchOutcome.Draw => "draw",
        _ => outcome.ToString().ToLowerInvariant()
    };

    private static MatchOutcome MatchOutcomeFromString(string value) => value switch
    {
        "not_played" => MatchOutcome.NotPlayed,
        "player1_won" => MatchOutcome.Player1Won,
        "player2_won" => MatchOutcome.Player2Won,
        "draw" => MatchOutcome.Draw,
        _ => Enum.Parse<MatchOutcome>(value, ignoreCase: true)
    };

    // Override SaveChanges to update UpdatedAt
    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker
            .Entries()
            .Where(e => e.Entity is User or Match);

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Modified)
            {
                ((dynamic)entry.Entity).UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
