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
    public DbSet<Block> Blocks { get; set; } = null!;
    public DbSet<RatingSnapshot> RatingSnapshots { get; set; } = null!;
    public DbSet<LinkedAccount> LinkedAccounts { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<Review> Reviews { get; set; } = null!;
    public DbSet<Gathering> Gatherings { get; set; } = null!;
    public DbSet<GatheringMember> GatheringMembers { get; set; } = null!;
    public DbSet<JoinRequest> JoinRequests { get; set; } = null!;
    public DbSet<JoinRequestRejection> JoinRequestRejections { get; set; } = null!;

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
        // GATHERINGS
        // =====================================================

        modelBuilder.Entity<Gathering>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.HostId);
            entity.HasIndex(e => e.EndsAt)
                .HasFilter("status IN ('open', 'gathered')");
            entity.HasIndex(e => e.Location)
                .HasMethod("GIST");

            entity.Property(e => e.Status)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<GatheringStatus>(v, ignoreCase: true))
                .HasMaxLength(20);

            entity.Property(e => e.VenueName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Note).HasColumnType("text");
            entity.Property(e => e.Location)
                .HasColumnType("geography (point, 4326)")
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.Host)
                .WithMany()
                .HasForeignKey(e => e.HostId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(t => t.HasCheckConstraint("ck_time_window", "ends_at > starts_at"));
        });

        modelBuilder.Entity<GatheringMember>(entity =>
        {
            entity.HasKey(e => new { e.GatheringId, e.UserId });

            entity.HasIndex(e => e.UserId);

            entity.Property(e => e.JoinedAt)
                .HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.Gathering)
                .WithMany(g => g.Members)
                .HasForeignKey(e => e.GatheringId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JoinRequest>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.GatheringId);
            entity.HasIndex(e => e.RequesterId);

            // At most one pending request per person per gathering
            entity.HasIndex(e => new { e.GatheringId, e.RequesterId })
                .IsUnique()
                .HasFilter("status = 'pending'");

            entity.Property(e => e.Status)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<JoinRequestStatus>(v, ignoreCase: true))
                .HasMaxLength(20);

            entity.Property(e => e.Message).HasMaxLength(500);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.Gathering)
                .WithMany(g => g.JoinRequests)
                .HasForeignKey(e => e.GatheringId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Requester)
                .WithMany()
                .HasForeignKey(e => e.RequesterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JoinRequestRejection>(entity =>
        {
            entity.HasKey(e => new { e.JoinRequestId, e.UserId });

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.JoinRequest)
                .WithMany(r => r.Rejections)
                .HasForeignKey(e => e.JoinRequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // =====================================================
        // REVIEWS
        // =====================================================

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.RevieweeId);
            entity.HasIndex(e => e.ReviewerId);

            // One review per reviewer, per person, per gathering
            entity.HasIndex(e => new { e.GatheringId, e.ReviewerId, e.RevieweeId })
                .IsUnique();

            entity.Property(e => e.Comment).HasColumnType("text");

            entity.Property(e => e.Tags)
                .HasConversion(
                    v => string.Join(",", v),
                    v => v.Split(",", StringSplitOptions.RemoveEmptyEntries).ToList(),
                    new ValueComparer<List<string>>(
                        (a, b) => a!.SequenceEqual(b!),
                        v => v.Aggregate(0, (h, t) => HashCode.Combine(h, t.GetHashCode())),
                        v => v.ToList()))
                .HasMaxLength(500);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.Gathering)
                .WithMany()
                .HasForeignKey(e => e.GatheringId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(t => t.HasCheckConstraint("ck_different_users", "reviewer_id != reviewee_id"));
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
            .Where(e => e.Entity is User or Gathering);

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Modified)
            {
                ((dynamic)entry.Entity).UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
