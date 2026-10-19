using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace ChessApp.Backend.Services;

public class LichessOptions
{
    public string BaseUrl { get; set; } = "https://lichess.org";

    // Lichess needs no app registration: any stable string identifies the client
    public string ClientId { get; set; } = "blitzmate";

    // Must match the deep link the mobile app handles (AndroidManifest intent-filter)
    public string RedirectUri { get; set; } = "com.chessmatchingapp://oauth/lichess";

    public string UserAgent { get; set; } = "BlitzMate/0.1";

    // Linked accounts older than this are re-fetched by LinkedAccountSyncWorker
    public int SyncIntervalHours { get; set; } = 12;
}

// Subset of the Lichess user object (GET /api/account, /api/user/{id}, POST /api/users)
public class LichessUser
{
    public string Id { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string? Title { get; set; }
    public string? Url { get; set; }
    public long? CreatedAt { get; set; } // unix ms
    public long? SeenAt { get; set; }    // unix ms
    public bool Disabled { get; set; }
    public Dictionary<string, LichessPerf>? Perfs { get; set; }
    public LichessCount? Count { get; set; }
}

public class LichessPerf
{
    public int Games { get; set; }
    public int Rating { get; set; }
    public int Prog { get; set; }
    public bool Prov { get; set; }
}

public class LichessCount
{
    public int All { get; set; }
    public int Win { get; set; }
    public int Loss { get; set; }
    public int Draw { get; set; }
}

/// <summary>Thrown when Lichess rejects the OAuth code or token.</summary>
public class LichessAuthException : Exception
{
    public LichessAuthException(string message) : base(message) { }
}

public interface ILichessClient
{
    string BuildAuthorizeUrl(string state, string codeChallenge);
    Task<string> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct = default);
    Task<LichessUser> GetAccountAsync(string accessToken, CancellationToken ct = default);
    Task RevokeTokenAsync(string accessToken, CancellationToken ct = default);
    Task<LichessUser?> GetUserAsync(string id, CancellationToken ct = default);
    Task<List<LichessUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default);
}

public class LichessClient : ILichessClient
{
    // POST /api/users accepts at most 300 ids per request
    public const int MaxUsersPerBatch = 300;

    private readonly HttpClient _http;
    private readonly LichessOptions _options;

    public LichessClient(HttpClient http, IOptions<LichessOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public string BuildAuthorizeUrl(string state, string codeChallenge)
    {
        // No scope requested: identity (GET /api/account) is all we need
        return QueryHelpers.AddQueryString($"{_options.BaseUrl}/oauth", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = codeChallenge,
            ["state"] = state,
        });
    }

    public async Task<string> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct = default)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = _options.RedirectUri,
            ["client_id"] = _options.ClientId,
        });

        using var response = await _http.PostAsync("/api/token", form, ct);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            throw new LichessAuthException("Lichess rejected the authorization code. Please try connecting again.");
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        if (string.IsNullOrEmpty(token?.AccessToken))
            throw new LichessAuthException("Lichess did not return an access token.");

        return token.AccessToken;
    }

    public async Task<LichessUser> GetAccountAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/account");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new LichessAuthException("Lichess access token was rejected.");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<LichessUser>(ct)
            ?? throw new LichessAuthException("Lichess returned an empty account.");
    }

    public async Task RevokeTokenAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var _ = await _http.SendAsync(request, ct);
    }

    public async Task<LichessUser?> GetUserAsync(string id, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"/api/user/{Uri.EscapeDataString(id)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LichessUser>(ct);
    }

    public async Task<List<LichessUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return new();
        if (ids.Count > MaxUsersPerBatch)
            throw new ArgumentException($"At most {MaxUsersPerBatch} ids per request", nameof(ids));

        var body = new StringContent(string.Join(",", ids), Encoding.UTF8, "text/plain");
        using var response = await _http.PostAsync("/api/users", body, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<LichessUser>>(ct) ?? new();
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }
}
