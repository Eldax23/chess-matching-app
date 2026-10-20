using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using ChessApp.Backend.DTOs;
using ChessApp.Backend.Services;

namespace ChessApp.Backend.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class AvailabilityController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<AvailabilityController> _logger;

    public AvailabilityController(
        IUserService userService,
        IHubContext<NotificationHub> hubContext,
        ILogger<AvailabilityController> logger)
    {
        _userService = userService;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Set user availability with location
    /// </summary>
    [HttpPost("set")]
    [ProducesResponseType(typeof(SetAvailabilityResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetAvailability([FromBody] SetAvailabilityRequest request)
    {
        try
        {
            var userId = GetUserId();
            var response = await _userService.SetAvailabilityAsync(userId, request);

            // Broadcast to all connected clients. Never include coordinates here:
            // every connected user receives this, wherever they are.
            await NotificationHub.BroadcastAvailabilityChange(_hubContext,
                new UserAvailabilityChangedPayload
                {
                    UserId = userId,
                    IsAvailable = request.IsAvailable,
                    HasBoard = request.HasBoard
                });

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting availability");
            return StatusCode(500, new ErrorResponse
            {
                Error = "InternalServerError",
                Message = "Failed to set availability"
            });
        }
    }

    /// <summary>
    /// Get nearby available users
    /// </summary>
    [HttpGet("nearby")]
    [ProducesResponseType(typeof(NearbyUsersResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNearby(
        [FromQuery] int radiusKm = 10,
        [FromQuery] bool? hasBoard = null,
        [FromQuery] int limit = 50)
    {
        try
        {
            var userId = GetUserId();
            var response = await _userService.FindNearbyUsersAsync(userId, radiusKm, hasBoard, limit);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "ValidationError",
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching nearby users");
            return StatusCode(500, new ErrorResponse
            {
                Error = "InternalServerError",
                Message = "Failed to fetch nearby users"
            });
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
