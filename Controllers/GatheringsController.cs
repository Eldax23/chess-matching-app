using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ChessApp.Backend.DTOs;
using ChessApp.Backend.Services;

namespace ChessApp.Backend.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class GatheringsController : ControllerBase
{
    private readonly IGatheringService _gatheringService;
    private readonly ILogger<GatheringsController> _logger;

    public GatheringsController(IGatheringService gatheringService, ILogger<GatheringsController> logger)
    {
        _gatheringService = gatheringService;
        _logger = logger;
    }

    /// <summary>
    /// Host a gathering at a venue for a time window
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(GatheringDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> Create([FromBody] CreateGatheringRequest request) =>
        Run("create gathering", async userId =>
            StatusCode(201, await _gatheringService.CreateAsync(userId, request)));

    /// <summary>
    /// Active gatherings near a point. Non-members see only an approximate area.
    /// </summary>
    [HttpGet("nearby")]
    [ProducesResponseType(typeof(GatheringsResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> GetNearby(
        [FromQuery] double latitude,
        [FromQuery] double longitude,
        [FromQuery] int radiusKm = 10,
        [FromQuery] int limit = 50)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            return Task.FromResult<IActionResult>(BadRequest(new ErrorResponse
            {
                Error = "ValidationError",
                Message = "Invalid coordinates"
            }));

        radiusKm = Math.Clamp(radiusKm, 1, 50);
        limit = Math.Clamp(limit, 1, 100);

        return Run("fetch nearby gatherings", async userId =>
            Ok(await _gatheringService.GetNearbyAsync(userId, latitude, longitude, radiusKm, limit)));
    }

    /// <summary>
    /// Gatherings you host or joined, current and from the last week
    /// </summary>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(GatheringsResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> GetMine() =>
        Run("fetch your gatherings", async userId => Ok(await _gatheringService.GetMineAsync(userId)));

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(GatheringDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Get(Guid id) =>
        Run("fetch gathering", async userId => Ok(await _gatheringService.GetAsync(id, userId)));

    /// <summary>
    /// Ask to join. One approval from anyone already in it lets you in.
    /// </summary>
    [HttpPost("{id}/join")]
    [ProducesResponseType(typeof(JoinRequestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> Join(Guid id, [FromBody] JoinGatheringRequest? request) =>
        Run("request to join", async userId =>
            StatusCode(201, await _gatheringService.RequestToJoinAsync(id, userId, request ?? new JoinGatheringRequest())));

    [HttpPost("{id}/leave")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Leave(Guid id) =>
        Run("leave gathering", async userId =>
        {
            await _gatheringService.LeaveAsync(id, userId);
            return NoContent();
        });

    /// <summary>
    /// Host only: end the gathering, which opens reviews
    /// </summary>
    [HttpPost("{id}/end")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> End(Guid id) =>
        Run("end gathering", async userId =>
        {
            await _gatheringService.EndAsync(id, userId);
            return NoContent();
        });

    /// <summary>
    /// Host only: call it off
    /// </summary>
    [HttpPost("{id}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Cancel(Guid id) =>
        Run("cancel gathering", async userId =>
        {
            await _gatheringService.CancelAsync(id, userId);
            return NoContent();
        });

    /// <summary>
    /// Pending requests to join gatherings you're in, that you haven't said no to
    /// </summary>
    [HttpGet("requests")]
    [ProducesResponseType(typeof(JoinRequestsResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> GetRequestsToApprove() =>
        Run("fetch join requests", async userId => Ok(await _gatheringService.GetRequestsToApproveAsync(userId)));

    /// <summary>
    /// Your own requests from the last day, with their status
    /// </summary>
    [HttpGet("requests/mine")]
    [ProducesResponseType(typeof(JoinRequestsResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> GetMyRequests() =>
        Run("fetch your join requests", async userId => Ok(await _gatheringService.GetMyRequestsAsync(userId)));

    [HttpPost("requests/{requestId}/approve")]
    [ProducesResponseType(typeof(JoinRequestDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Approve(Guid requestId) =>
        Run("approve join request", async userId => Ok(await _gatheringService.ApproveAsync(requestId, userId)));

    [HttpPost("requests/{requestId}/reject")]
    [ProducesResponseType(typeof(JoinRequestDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Reject(Guid requestId) =>
        Run("reject join request", async userId => Ok(await _gatheringService.RejectAsync(requestId, userId)));

    [HttpPost("requests/{requestId}/withdraw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Withdraw(Guid requestId) =>
        Run("withdraw join request", async userId =>
        {
            await _gatheringService.WithdrawAsync(requestId, userId);
            return NoContent();
        });

    private async Task<IActionResult> Run(string action, Func<Guid, Task<IActionResult>> body)
    {
        try
        {
            return await body(GetUserId());
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
            _logger.LogError(ex, "Failed to {Action}", action);
            return StatusCode(500, new ErrorResponse { Error = "InternalServerError", Message = $"Failed to {action}" });
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
