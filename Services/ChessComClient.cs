using System.Net;
using System.Text.Json.Serialization;

namespace ChessApp.Backend.Services;

public class ChessComOptions
{
    public string BaseUrl { get; set; } = "https://api.chess.com";

    // chess.com asks API clients to identify themselves with a contact in the User-Agent
    public string UserAgent { get; set; } = "BlitzMate/0.1";

    public int SyncIntervalHours { get; set; } = 12;
}

// GET /pub/player/{username}
public class ChessComPlayer
{
    [JsonPropertyName("player_id")]
    public long PlayerId { get; set; }
    public string Username { get; set; } = null!; // always lowercase
    public string? Url { get; set; }              // https://www.chess.com/member/{DisplayCase}
    public string? Title { get; set; }
    public long? Joined { get; set; }             // unix seconds
    [JsonPropertyName("last_online")]
    public long? LastOnline { get; set; }         // unix seconds
    public string? Status { get; set; }           // "closed", "closed:fair_play_violations", ...
}

// GET /pub/player/{username}/stats: one entry per rating category
public class ChessComStats
{
    [JsonPropertyName("chess_bullet")] public ChessComCategory? Bullet { get; set; }
    [JsonPropertyName("chess_blitz")] public ChessComCategory? Blitz { get; set; }
    [JsonPropertyName("chess_rapid")] public ChessComCategory? Rapid { get; set; }
    [JsonPropertyName("chess_daily")] public ChessComCategory? Daily { get; set; }
    [JsonPropertyName("chess960_daily")] public ChessComCategory? Chess960Daily { get; set; }
}

public class ChessComCategory
{
    public ChessComRatingPoint? Last { get; set; }
    public ChessComRatingPoint? Best { get; set; }
    public ChessComRecord? Record { get; set; }
}

public class ChessComRatingPoint
{
    public int Rating { get; set; }
}

public class ChessComRecord
{
    public int Win { get; set; }
    public int Loss { get; set; }
    public int Draw { get; set; }
}

public interface IChessComClient
{
    Task<ChessComPlayer?> GetPlayerAsync(string username, CancellationToken ct = default);
    Task<ChessComStats> GetStatsAsync(string username, CancellationToken ct = default);
}

public class ChessComClient : IChessComClient
{
    private readonly HttpClient _http;

    public ChessComClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ChessComPlayer?> GetPlayerAsync(string username, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"/pub/player/{Path(username)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChessComPlayer>(ct);
    }

    public async Task<ChessComStats> GetStatsAsync(string username, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"/pub/player/{Path(username)}/stats", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChessComStats>(ct) ?? new();
    }

    // The API rejects anything but the lowercase username
    private static string Path(string username) => Uri.EscapeDataString(username.ToLowerInvariant());
}
