using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ChessApp.Backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ChessApp.Backend.DTOs;
using ChessApp.Backend.Services;

namespace ChessApp.Backend.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILichessIntegrationService _lichess;
    private readonly IEmailVerificationService _verification;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        ILichessIntegrationService lichess,
        IEmailVerificationService verification,
        ApplicationDbContext db,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _lichess = lichess;
        _verification = verification;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Register a new user
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var (user, accessToken, refreshToken) = await _authService.RegisterAsync(
                request.Email,
                request.Username,
                request.Password,
                request.FullName);

            // The account exists either way; if the email can't go out now the
            // app's "send again" button retries
            try
            {
                await _verification.SendCodeAsync(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not send the verification code to new user {UserId}", user.Id);
            }

            var response = new AuthResponse
            {
                UserId = user.Id,
                Email = user.Email,
                Username = user.Username,
                EmailVerified = user.IsEmailVerified,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = 3600
            };

            return CreatedAtAction(nameof(Register), response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Registration failed: {Message}", ex.Message);
            return BadRequest(new ErrorResponse
            {
                Error = "RegistrationError",
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration error");
            return StatusCode(500, new ErrorResponse
            {
                Error = "InternalServerError",
                Message = "An unexpected error occurred"
            });
        }
    }

    /// <summary>
    /// Login with email and password
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var (user, accessToken, refreshToken) = await _authService.LoginAsync(
                request.Email,
                request.Password);

            var response = new AuthResponse
            {
                UserId = user.Id,
                Email = user.Email,
                Username = user.Username,
                EmailVerified = user.IsEmailVerified,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = 3600
            };

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Login failed: {Message}", ex.Message);
            return Unauthorized(new ErrorResponse
            {
                Error = "AuthenticationError",
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error");
            return StatusCode(500, new ErrorResponse
            {
                Error = "InternalServerError",
                Message = "An unexpected error occurred"
            });
        }
    }

    /// <summary>
    /// Start signing in with Lichess. Open the returned URL in a browser;
    /// Lichess redirects back to the app's deep link with ?code=...&amp;state=...
    /// </summary>
    [HttpPost("lichess/start")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LichessAuthorizeResponse), StatusCodes.Status200OK)]
    public IActionResult StartLichessSignIn()
    {
        return Ok(_lichess.StartSignIn());
    }

    /// <summary>
    /// Finish signing in with Lichess: signs in the player who linked that
    /// Lichess account, or creates a new player for it
    /// </summary>
    [HttpPost("lichess/callback")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LichessSignIn([FromBody] LichessCallbackRequest request, CancellationToken ct)
    {
        try
        {
            var user = await _lichess.CompleteSignInAsync(request.Code, request.State, ct);
            var (accessToken, refreshToken) = await _authService.IssueTokensAsync(user);

            return Ok(new AuthResponse
            {
                UserId = user.Id,
                Email = user.Email,
                Username = user.Username,
                EmailVerified = user.IsEmailVerified,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = 3600
            });
        }
        catch (LichessAuthException ex)
        {
            return BadRequest(new ErrorResponse { Error = "LichessAuthFailed", Message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new ErrorResponse { Error = "AuthenticationError", Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse { Error = "EmailAlreadyRegistered", Message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Lichess unreachable during sign-in");
            return StatusCode(502, new ErrorResponse { Error = "LichessUnavailable", Message = "Could not reach Lichess. Please try again." });
        }
    }

    /// <summary>
    /// Confirm the signed-in player's email with the 6-digit code we emailed
    /// </summary>
    [HttpPost("email/verify")]
    [Authorize]
    [ProducesResponseType(typeof(EmailVerificationStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken ct)
    {
        try
        {
            await _verification.VerifyAsync(GetUserId(), request.Code, ct);
            return Ok(new EmailVerificationStatusResponse { EmailVerified = true });
        }
        catch (EmailVerificationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Error, Message = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return Unauthorized(new ErrorResponse { Error = "AuthenticationError", Message = "Please sign in again." });
        }
    }

    /// <summary>
    /// Email a new verification code (at most once a minute)
    /// </summary>
    [HttpPost("email/resend")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendVerificationEmail(CancellationToken ct)
    {
        var userId = GetUserId();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null)
            return Unauthorized(new ErrorResponse { Error = "AuthenticationError", Message = "Please sign in again." });

        try
        {
            await _verification.SendCodeAsync(user, ct);
            return NoContent();
        }
        catch (EmailVerificationException ex) when (ex.RetryAfterSeconds is { } wait)
        {
            Response.Headers.RetryAfter = wait.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new ErrorResponse { Error = ex.Error, Message = ex.Message });
        }
        catch (EmailVerificationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Error, Message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Could not send the verification code to user {UserId}", userId);
            return StatusCode(502, new ErrorResponse { Error = "EmailUnavailable", Message = "We couldn't send the email. Please try again soon." });
        }
    }

    /// <summary>
    /// Refresh access token using refresh token
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RefreshTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        try
        {
            var accessToken = await _authService.RefreshAccessTokenAsync(request.RefreshToken);

            return Ok(new RefreshTokenResponse
            {
                AccessToken = accessToken,
                ExpiresIn = 3600
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new ErrorResponse
            {
                Error = "InvalidToken",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Logout (revoke refresh token)
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        try
        {
            await _authService.RevokeRefreshTokenAsync(request.RefreshToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logout error");
            return StatusCode(500, new ErrorResponse
            {
                Error = "InternalServerError",
                Message = "Logout failed"
            });
        }
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User ID not found in token"));
}
