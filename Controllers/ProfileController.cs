using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ChessApp.Backend.DTOs;
using ChessApp.Backend.Services;

namespace ChessApp.Backend.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(IUserService userService, ILogger<ProfileController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Get current user's profile
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var userId = GetUserId();
            var profile = await _userService.GetProfileAsync(userId);
            return Ok(profile);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching profile");
            return StatusCode(500, new ErrorResponse
            {
                Error = "InternalServerError",
                Message = "Failed to fetch profile"
            });
        }
    }

    /// <summary>
    /// Get public profile of another user
    /// </summary>
    [HttpGet("{userId}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(UserPublicProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublicProfile(Guid userId)
    {
        try
        {
            var profile = await _userService.GetPublicProfileAsync(userId);
            return Ok(profile);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Update current user's profile
    /// </summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        try
        {
            var userId = GetUserId();
            var profile = await _userService.UpdateProfileAsync(userId, request);
            return Ok(profile);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile");
            return StatusCode(500, new ErrorResponse
            {
                Error = "InternalServerError",
                Message = "Failed to update profile"
            });
        }
    }

    /// <summary>
    /// Upload profile photo
    /// </summary>
    [HttpPost("me/photo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadPhoto(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new ErrorResponse
                {
                    Error = "ValidationError",
                    Message = "No file provided"
                });

            if (file.Length > 5 * 1024 * 1024) // 5 MB
                return BadRequest(new ErrorResponse
                {
                    Error = "ValidationError",
                    Message = "File size exceeds 5 MB limit"
                });

            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(file.ContentType))
                return BadRequest(new ErrorResponse
                {
                    Error = "ValidationError",
                    Message = "Only JPEG, PNG, and WebP images are allowed"
                });

            var userId = GetUserId();
            using (var stream = file.OpenReadStream())
            {
                var photoUrl = await _userService.UploadPhotoAsync(userId, stream, file.ContentType);
                return Ok(new { photoUrl });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading photo");
            return StatusCode(500, new ErrorResponse
            {
                Error = "InternalServerError",
                Message = "Failed to upload photo"
            });
        }
    }

    // =====================================================
    // HELPER METHODS
    // =====================================================

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                         User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
            throw new UnauthorizedAccessException("Invalid user identity");

        return userId;
    }
}
