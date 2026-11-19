using System.Security.Cryptography;
using System.Text;
using ChessApp.Backend.Data;
using ChessApp.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace ChessApp.Backend.Services;

public class EmailVerificationException : Exception
{
    public string Error { get; }
    public int? RetryAfterSeconds { get; }

    public EmailVerificationException(string error, string message, int? retryAfterSeconds = null) : base(message)
    {
        Error = error;
        RetryAfterSeconds = retryAfterSeconds;
    }
}

public interface IEmailVerificationService
{
    /// <summary>Emails a fresh 6-digit code. Throws if one was sent too recently.</summary>
    Task SendCodeAsync(User user, CancellationToken ct = default);

    /// <summary>Checks the code and marks the email verified.</summary>
    Task VerifyAsync(Guid userId, string code, CancellationToken ct = default);

    /// <summary>Deletes password accounts that never verified within <paramref name="maxAge"/>.</summary>
    Task<int> DeleteAbandonedAccountsAsync(TimeSpan maxAge, CancellationToken ct = default);
}

public class EmailVerificationService : IEmailVerificationService
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public const int MaxAttempts = 5;

    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _sender;
    private readonly ILogger<EmailVerificationService> _logger;

    public EmailVerificationService(ApplicationDbContext db, IEmailSender sender, ILogger<EmailVerificationService> logger)
    {
        _db = db;
        _sender = sender;
        _logger = logger;
    }

    public async Task SendCodeAsync(User user, CancellationToken ct = default)
    {
        if (user.IsEmailVerified)
            throw new EmailVerificationException("AlreadyVerified", "Your email is already confirmed.");

        var now = DateTime.UtcNow;
        if (user.EmailVerificationCodeSentAt is { } sentAt && now - sentAt < ResendCooldown)
        {
            var wait = (int)Math.Ceiling((ResendCooldown - (now - sentAt)).TotalSeconds);
            throw new EmailVerificationException("TooSoon", $"Please wait {wait} seconds before asking for another code.", wait);
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        user.EmailVerificationCodeHash = Hash(user.Id, code);
        user.EmailVerificationCodeExpiresAt = now + CodeLifetime;
        user.EmailVerificationCodeSentAt = now;
        user.EmailVerificationFailedAttempts = 0;
        user.EmailVerificationRequestedAt ??= now;
        await _db.SaveChangesAsync(ct);

        await _sender.SendAsync(
            user.Email,
            $"{code} is your BlitzMate code",
            $"Hi {user.Username},\n\n" +
            $"Your BlitzMate confirmation code is {code}\n\n" +
            $"Enter it in the app to confirm your email. It expires in {CodeLifetime.TotalMinutes:0} minutes.\n\n" +
            "If you didn't sign up for BlitzMate, you can ignore this email.",
            ct);

        _logger.LogInformation("Sent email verification code to user {UserId}", user.Id);
    }

    public async Task VerifyAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException($"User {userId} not found");

        if (user.IsEmailVerified)
            return;

        if (user.EmailVerificationCodeHash == null)
            throw new EmailVerificationException("NoCode", "Ask for a new code first.");

        if (user.EmailVerificationCodeExpiresAt < DateTime.UtcNow)
            throw new EmailVerificationException("CodeExpired", "This code has expired. Ask for a new one.");

        if (user.EmailVerificationFailedAttempts >= MaxAttempts)
            throw new EmailVerificationException("TooManyAttempts", "Too many wrong tries. Ask for a new code.");

        var expected = Convert.FromBase64String(user.EmailVerificationCodeHash);
        var actual = Convert.FromBase64String(Hash(userId, (code ?? "").Trim()));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            user.EmailVerificationFailedAttempts++;
            await _db.SaveChangesAsync(ct);
            var left = MaxAttempts - user.EmailVerificationFailedAttempts;
            throw left > 0
                ? new EmailVerificationException("InvalidCode", $"That code isn't right. {left} {(left == 1 ? "try" : "tries")} left.")
                : new EmailVerificationException("TooManyAttempts", "Too many wrong tries. Ask for a new code.");
        }

        MarkVerified(user);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("User {UserId} confirmed their email", userId);
    }

    public async Task<int> DeleteAbandonedAccountsAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var abandoned = await _db.Users
            .Where(u => u.EmailVerifiedAt == null
                && u.PasswordHash != ""
                && u.EmailVerificationRequestedAt != null
                && u.EmailVerificationRequestedAt < cutoff)
            .ToListAsync(ct);

        if (abandoned.Count == 0)
            return 0;

        _db.Users.RemoveRange(abandoned);
        await _db.SaveChangesAsync(ct);
        return abandoned.Count;
    }

    public static void MarkVerified(User user)
    {
        user.EmailVerifiedAt = DateTime.UtcNow;
        user.EmailVerificationCodeHash = null;
        user.EmailVerificationCodeExpiresAt = null;
        user.EmailVerificationFailedAttempts = 0;
        user.UpdatedAt = DateTime.UtcNow;
    }

    private static string Hash(Guid userId, string code) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{code}")));
}

/// <summary>
/// Hourly: deletes password accounts whose email was never confirmed within a
/// week, so an address someone typed without owning it is freed up again.
/// </summary>
public class UnverifiedAccountCleanupWorker : BackgroundService
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UnverifiedAccountCleanupWorker> _logger;

    public UnverifiedAccountCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<UnverifiedAccountCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var deleted = await scope.ServiceProvider.GetRequiredService<IEmailVerificationService>()
                    .DeleteAbandonedAccountsAsync(MaxAge, stoppingToken);
                if (deleted > 0)
                    _logger.LogInformation("Deleted {Count} accounts that never confirmed their email", deleted);
            }
            catch (OperationCanceledException)
            {
                // Shutting down
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unverified account cleanup failed");
            }
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }
}
