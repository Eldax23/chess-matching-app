using Microsoft.EntityFrameworkCore;
using ChessApp.Backend.Data;
using ChessApp.Backend.DTOs;
using ChessApp.Backend.Models;

namespace ChessApp.Backend.Services;

public interface IReviewService
{
    Task<ReviewDto> CreateReviewAsync(Guid reviewerId, CreateReviewRequest request);

    Task<ReviewListResponse> GetReviewsForUserAsync(Guid userId, int limit = 20, int offset = 0);

    Task<ReviewListResponse> GetReviewsByUserAsync(Guid userId, int limit = 20, int offset = 0);
}

public class ReviewService : IReviewService
{
    // Reviews open when a gathering ends and stay open for a week
    public static readonly TimeSpan ReviewWindow = TimeSpan.FromDays(7);

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ReviewService> _logger;

    public ReviewService(ApplicationDbContext dbContext, ILogger<ReviewService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ReviewDto> CreateReviewAsync(Guid reviewerId, CreateReviewRequest request)
    {
        var gathering = await _dbContext.Gatherings
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == request.GatheringId)
            ?? throw new KeyNotFoundException($"Gathering {request.GatheringId} not found");

        if (!gathering.Members.Any(m => m.UserId == reviewerId))
            throw new UnauthorizedAccessException("Only people in this gathering can leave reviews");

        if (reviewerId == request.RevieweeId)
            throw new InvalidOperationException("You can't review yourself");

        if (!gathering.Members.Any(m => m.UserId == request.RevieweeId))
            throw new InvalidOperationException("You can only review people who were in this gathering");

        var endedAt = gathering.Status == GatheringStatus.Ended ? gathering.EndedAt ?? gathering.EndsAt : (DateTime?)null;
        if (endedAt == null)
            throw new InvalidOperationException("Reviews open once the gathering has ended");

        if (DateTime.UtcNow > endedAt.Value + ReviewWindow)
            throw new InvalidOperationException("Reviews for this gathering have closed");

        var alreadyReviewed = await _dbContext.Reviews.AnyAsync(r =>
            r.GatheringId == request.GatheringId &&
            r.ReviewerId == reviewerId &&
            r.RevieweeId == request.RevieweeId);

        if (alreadyReviewed)
            throw new InvalidOperationException("You have already reviewed this person for this gathering");

        var review = new Review
        {
            Id = Guid.NewGuid(),
            GatheringId = request.GatheringId,
            ReviewerId = reviewerId,
            RevieweeId = request.RevieweeId,
            Tags = request.Tags.Distinct().ToList(),
            NoShow = request.NoShow,
            Comment = request.Comment,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Reviews.Add(review);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Review created: {ReviewId} for gathering {GatheringId} by {ReviewerId} of {RevieweeId}",
            review.Id, request.GatheringId, reviewerId, request.RevieweeId);

        var reviewer = await _dbContext.Users.FindAsync(reviewerId);

        return MapToDto(review, reviewer!);
    }

    public async Task<ReviewListResponse> GetReviewsForUserAsync(Guid userId, int limit = 20, int offset = 0)
    {
        var query = _dbContext.Reviews
            .Include(r => r.Reviewer)
            .Where(r => r.RevieweeId == userId);

        // Tags live in one column, so count them in memory; a person's reviews are few
        var allTags = await query.Select(r => r.Tags).ToListAsync();
        var tagCounts = allTags
            .SelectMany(tags => tags)
            .GroupBy(tag => tag)
            .Select(g => new ReviewTagCountDto { Tag = g.Key, Count = g.Count() })
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Tag)
            .ToList();

        var reviews = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return new ReviewListResponse
        {
            Reviews = reviews.Select(r => MapToDto(r, r.Reviewer)).ToList(),
            TagCounts = tagCounts,
            NoShowCount = await query.CountAsync(r => r.NoShow)
        };
    }

    public async Task<ReviewListResponse> GetReviewsByUserAsync(Guid userId, int limit = 20, int offset = 0)
    {
        var reviews = await _dbContext.Reviews
            .Include(r => r.Reviewer)
            .Where(r => r.ReviewerId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return new ReviewListResponse
        {
            Reviews = reviews.Select(r => MapToDto(r, r.Reviewer)).ToList()
        };
    }

    private static ReviewDto MapToDto(Review review, User reviewer)
    {
        return new ReviewDto
        {
            Id = review.Id,
            GatheringId = review.GatheringId,
            ReviewerId = review.ReviewerId,
            ReviewerUsername = reviewer.Username,
            ReviewerPhotoUrl = reviewer.PhotoUrl,
            RevieweeId = review.RevieweeId,
            Tags = review.Tags,
            NoShow = review.NoShow,
            Comment = review.Comment,
            CreatedAt = review.CreatedAt
        };
    }
}
