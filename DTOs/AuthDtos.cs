namespace ChessApp.Backend.DTOs;

public class RegisterRequest
{
    public string Email { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string? FullName { get; set; }
}

public class LoginRequest
{
    public string Email { get; set; } = null!;
    public string Password { get; set; } = null!;
}

public class AuthResponse
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = null!;
    public string Username { get; set; } = null!;
    // Until true, the app shows the "enter your code" screen and meeting
    // features (nearby, gatherings, reviews) answer 403 EmailNotVerified
    public bool EmailVerified { get; set; }
    public string AccessToken { get; set; } = null!;
    public string RefreshToken { get; set; } = null!;
    public int ExpiresIn { get; set; } = 3600;
}

public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = null!;
}

public class RefreshTokenResponse
{
    public string AccessToken { get; set; } = null!;
    public int ExpiresIn { get; set; } = 3600;
}

public class VerifyEmailRequest
{
    public string Code { get; set; } = null!;
}

public class EmailVerificationStatusResponse
{
    public bool EmailVerified { get; set; }
}
