namespace ChessApp.Backend.Models;

/// <summary>
/// What one person thought of another after a gathering. It's about the
/// person and the meetup, never about chess results, and there is no score.
/// </summary>
public class Review
{
    public Guid Id { get; set; }

    public Guid GatheringId { get; set; }
    public virtual Gathering Gathering { get; set; } = null!;

    public Guid ReviewerId { get; set; }
    public virtual User Reviewer { get; set; } = null!;

    public Guid RevieweeId { get; set; }
    public virtual User Reviewee { get; set; } = null!;

    // Quick tags from ReviewTags.All
    public List<string> Tags { get; set; } = new();

    // They were in the gathering but didn't come
    public bool NoShow { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class ReviewTags
{
    public const string Friendly = "friendly";
    public const string OnTime = "on_time";
    public const string GoodCompany = "good_company";
    public const string PatientWithBeginners = "patient_with_beginners";

    public static readonly IReadOnlyList<string> All = new[] { Friendly, OnTime, GoodCompany, PatientWithBeginners };
}
