using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ChessApp.Backend.DTOs;
using ChessApp.Backend.Services;

namespace ChessApp.Backend.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;
    private readonly ILogger<ReviewsController> _logger;

    public ReviewsController(IReviewService reviewService, ILogger<ReviewsController> logger)
    {
        _reviewService = reviewService;
        _logger = logger;
    }

    /// <summary>
    /// Leave a review for the other player in a completed match
    /// </summary>
    [HttpPost]
    [Authorize(Policy = EmailVerifiedRequirement.PolicyName)]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateReview([FromBody] CreateReviewRequest request)
    {
        try
        {
            var userId = GetUserId();
            var response = await _reviewService.CreateReviewAsync(userId, request);
            return CreatedAtAction(nameof(CreateReview), response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Error = "NotFound", Message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new ErrorResponse { Error = "Forbidden", Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = "ValidationError", Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating review");
            return StatusCode(500, new ErrorResponse { Error = "InternalServerError", Message = "Failed to create review" });
        }
    }

    /// <summary>
    /// Get reviews a user has received, plus their average rating
    /// </summary>
    [HttpGet("user/{userId}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ReviewListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReviewsForUser(
        Guid userId,
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0)
    {
        try
        {
            var response = await _reviewService.GetReviewsForUserAsync(userId, limit, offset);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching reviews for user {UserId}", userId);
            return StatusCode(500, new ErrorResponse { Error = "InternalServerError", Message = "Failed to fetch reviews" });
        }
    }

    /// <summary>
    /// Get reviews the current user has written
    /// </summary>
    [HttpGet("given")]
    [ProducesResponseType(typeof(ReviewListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGivenReviews(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0)
    {
        try
        {
            var userId = GetUserId();
            var response = await _reviewService.GetReviewsByUserAsync(userId, limit, offset);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching given reviews");
            return StatusCode(500, new ErrorResponse { Error = "InternalServerError", Message = "Failed to fetch reviews" });
        }
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                         User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
            throw new UnauthorizedAccessException("Invalid user identity");

        return userId;
    }
}
