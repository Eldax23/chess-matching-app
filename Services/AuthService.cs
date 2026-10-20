using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ChessApp.Backend.Models;
using ChessApp.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessApp.Backend.Services;

public interface IAuthService
{
    Task<(User user, string accessToken, string refreshToken)> RegisterAsync(
        string email, string username, string password, string? fullName);
    
    Task<(string accessToken, string refreshToken)> LoginAsync(string email, string password);
    
    Task<string> RefreshAccessTokenAsync(string refreshToken);
    
    Task RevokeRefreshTokenAsync(string refreshToken);
    
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
}

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(User user, string accessToken, string refreshToken)> RegisterAsync(
        string email, string username, string password, string? fullName)
    {
        // Check if user already exists
        var existingUser = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == email || u.Username == username);

        if (existingUser != null)
        {
            throw new InvalidOperationException("Email or username already exists");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email.ToLower(),
            Username = username,
            PasswordHash = HashPassword(password),
            FullName = fullName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("User registered: {Email}", email);

        var accessToken = GenerateAccessToken(user);
        var refreshToken = await GenerateRefreshTokenAsync(user.Id);

        return (user, accessToken, refreshToken);
    }

    public async Task<(string accessToken, string refreshToken)> LoginAsync(
        string email, string password)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == email.ToLower());

        if (user == null || !VerifyPassword(password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedAccessException("User account is inactive");
        }

        // Update last login
        user.LastLogin = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("User logged in: {Email}", email);

        var accessToken = GenerateAccessToken(user);
        var refreshToken = await GenerateRefreshTokenAsync(user.Id);

        return (accessToken, refreshToken);
    }

    public async Task<string> RefreshAccessTokenAsync(string refreshToken)
    {
        var tokenHash = HashRefreshToken(refreshToken);

        var storedToken = await _dbContext.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (storedToken == null || !storedToken.IsValid)
        {
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        var user = storedToken.User;
        if (!user.IsActive)
        {
            throw new UnauthorizedAccessException("User account is inactive");
        }

        var newAccessToken = GenerateAccessToken(user);
        return newAccessToken;
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        var tokenHash = HashRefreshToken(refreshToken);

        var storedToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (storedToken != null)
        {
            storedToken.RevokedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Refresh token revoked for user: {UserId}", storedToken.UserId);
        }
    }

    public string HashPassword(string password)
    {
        // BCrypt automatically generates and embeds a random salt per-password,
        // and its work factor makes brute-forcing deliberately expensive - unlike
        // the previous plain SHA-512 hash, which was unsalted (identical passwords
        // produced identical hashes, and precomputed rainbow tables applied) and
        // fast (cheap to brute-force offline if the hash ever leaked).
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }

    public bool VerifyPassword(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }

    private string GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_configuration["Jwt:SecretKey"] ?? throw new InvalidOperationException("JWT secret not configured")));

        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim("FullName", user.FullName ?? "")
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<string> GenerateRefreshTokenAsync(Guid userId)
    {
        var randomBytes = new byte[64];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }

        var refreshToken = Convert.ToBase64String(randomBytes);
        var tokenHash = HashRefreshToken(refreshToken);

        var storedToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.RefreshTokens.Add(storedToken);
        await _dbContext.SaveChangesAsync();

        return refreshToken;
    }

    private string HashRefreshToken(string token)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
            return Convert.ToBase64String(hashedBytes);
        }
    }
}
