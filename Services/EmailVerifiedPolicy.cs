using System.Security.Claims;
using ChessApp.Backend.Data;
using ChessApp.Backend.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.EntityFrameworkCore;

namespace ChessApp.Backend.Services;

/// <summary>
/// Meeting people in person (nearby players, gatherings, reviews) needs a
/// confirmed email. Checked against the database rather than a token claim,
/// so it takes effect the moment the code is entered.
/// </summary>
public class EmailVerifiedRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "EmailVerified";
}

public class EmailVerifiedHandler : AuthorizationHandler<EmailVerifiedRequirement>
{
    private readonly ApplicationDbContext _db;

    public EmailVerifiedHandler(ApplicationDbContext db) => _db = db;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, EmailVerifiedRequirement requirement)
    {
        var id = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(id, out var userId)
            && await _db.Users.AnyAsync(u => u.Id == userId && u.EmailVerifiedAt != null))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>
/// Answers a failed EmailVerified check with a body the app can recognise,
/// instead of an empty 403.
/// </summary>
public class EmailVerifiedResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<EmailVerifiedRequirement>().Any() == true)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ErrorResponse
            {
                Error = "EmailNotVerified",
                Message = "Confirm your email to meet other players."
            });
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
