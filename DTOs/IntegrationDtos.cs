namespace ChessApp.Backend.DTOs;

public class LichessAuthorizeResponse
{
    public string AuthorizeUrl { get; set; } = null!;
    public string State { get; set; } = null!;
}

public class LichessCallbackRequest
{
    public string Code { get; set; } = null!;
    public string State { get; set; } = null!;
}

public class LinkChessComRequest
{
    public string Username { get; set; } = null!;
}

public class PlatformRatingDto
{
    public string Category { get; set; } = null!;
    public int Rating { get; set; }
    public int? Best { get; set; }
    public int Games { get; set; }
    public bool Provisional { get; set; }
    public int? Progress { get; set; }
}

public class LinkedAccountDto
{
    public string Platform { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string ProfileUrl { get; set; } = null!;
    public string? Title { get; set; }
    public bool Verified { get; set; }

    public List<PlatformRatingDto> Ratings { get; set; } = new();

    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }

    public DateTime? MemberSince { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime LastSyncedAt { get; set; }
}
