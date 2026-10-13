using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ChessApp.Backend.DTOs;
using ChessApp.Backend.Services;

namespace ChessApp.Backend.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class IntegrationsController : ControllerBase
{
    private readonly ILichessIntegrationService _lichess;
    private readonly IChessComIntegrationService _chessCom;
    private readonly ILogger<IntegrationsController> _logger;

    public IntegrationsController(
        ILichessIntegrationService lichess,
        IChessComIntegrationService chessCom,
        ILogger<IntegrationsController> logger)
    {
        _lichess = lichess;
        _chessCom = chessCom;
        _logger = logger;
    }

    /// <summary>
    /// Start linking a Lichess account. Open the returned URL in a browser;
    /// Lichess redirects back to the app's deep link with ?code=...&amp;state=...
    /// </summary>
    [HttpPost("lichess/authorize")]
    [ProducesResponseType(typeof(LichessAuthorizeResponse), StatusCodes.Status200OK)]
    public IActionResult AuthorizeLichess()
    {
        return Ok(_lichess.StartAuthorization(GetUserId()));
    }

    /// <summary>
    /// Finish linking: exchange the code from the deep link and store the account
    /// </summary>
    [HttpPost("lichess/callback")]
    [ProducesResponseType(typeof(LinkedAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LichessCallback([FromBody] LichessCallbackRequest request, CancellationToken ct)
    {
        try
        {
            var account = await _lichess.CompleteAuthorizationAsync(GetUserId(), request.Code, request.State, ct);
            return Ok(account);
        }
        catch (LichessAuthException ex)
        {
            return BadRequest(new ErrorResponse { Error = "LichessAuthFailed", Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse { Error = "AccountAlreadyLinked", Message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Lichess unreachable during OAuth callback");
            return StatusCode(502, new ErrorResponse { Error = "LichessUnavailable", Message = "Could not reach Lichess. Please try again." });
        }
    }

    /// <summary>
    /// Re-fetch ratings for the linked Lichess account (at most every 5 minutes)
    /// </summary>
    [HttpPost("lichess/refresh")]
    [ProducesResponseType(typeof(LinkedAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RefreshLichess(CancellationToken ct)
    {
        try
        {
            return Ok(await _lichess.RefreshAsync(GetUserId(), ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Error = "NotFound", Message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Lichess unreachable during refresh");
            return StatusCode(502, new ErrorResponse { Error = "LichessUnavailable", Message = "Could not reach Lichess. Please try again." });
        }
    }

    /// <summary>
    /// Unlink the Lichess account and remove its ratings from the profile
    /// </summary>
    [HttpDelete("lichess")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnlinkLichess(CancellationToken ct)
    {
        await _lichess.UnlinkAsync(GetUserId(), ct);
        return NoContent();
    }

    // =====================================================
    // CHESS.COM (username only, unverified until chess.com OAuth)
    // =====================================================

    /// <summary>
    /// Link (or replace) the chess.com account by username and fetch its ratings
    /// </summary>
    [HttpPut("chesscom")]
    [ProducesResponseType(typeof(LinkedAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LinkChessCom([FromBody] LinkChessComRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _chessCom.LinkAsync(GetUserId(), request.Username, ct));
        }
        catch (KeyNotFoundException ex)
        {
            return BadRequest(new ErrorResponse { Error = "PlayerNotFound", Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = "AccountClosed", Message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "chess.com unreachable while linking");
            return StatusCode(502, new ErrorResponse { Error = "ChessComUnavailable", Message = "Could not reach chess.com. Please try again." });
        }
    }

    /// <summary>
    /// Re-fetch ratings for the linked chess.com account (at most every 5 minutes)
    /// </summary>
    [HttpPost("chesscom/refresh")]
    [ProducesResponseType(typeof(LinkedAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RefreshChessCom(CancellationToken ct)
    {
        try
        {
            return Ok(await _chessCom.RefreshAsync(GetUserId(), ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Error = "NotFound", Message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "chess.com unreachable during refresh");
            return StatusCode(502, new ErrorResponse { Error = "ChessComUnavailable", Message = "Could not reach chess.com. Please try again." });
        }
    }

    /// <summary>
    /// Unlink the chess.com account and remove its ratings from the profile
    /// </summary>
    [HttpDelete("chesscom")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnlinkChessCom(CancellationToken ct)
    {
        await _chessCom.UnlinkAsync(GetUserId(), ct);
        return NoContent();
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
