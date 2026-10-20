namespace ChessApp.Backend.DTOs;

public class CreateReviewRequest
{
    public Guid GatheringId { get; set; }
    public Guid RevieweeId { get; set; }

    // Any of: friendly, on_time, good_company, patient_with_beginners
    public List<string> Tags { get; set; } = new();

    // They were in the gathering but didn't come
    public bool NoShow { get; set; }

    public string? Comment { get; set; }
}

public class ReviewDto
{
    public Guid Id { get; set; }
    public Guid GatheringId { get; set; }
    public Guid ReviewerId { get; set; }
    public string ReviewerUsername { get; set; } = null!;
    public string? ReviewerPhotoUrl { get; set; }
    public Guid RevieweeId { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool NoShow { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ReviewListResponse
{
    public List<ReviewDto> Reviews { get; set; } = new();
    public int Count => Reviews.Count;

    // How many reviews carry each tag, most common first. No score or average.
    public List<ReviewTagCountDto> TagCounts { get; set; } = new();
    public int NoShowCount { get; set; }
}

public class ReviewTagCountDto
{
    public string Tag { get; set; } = null!;
    public int Count { get; set; }
}
