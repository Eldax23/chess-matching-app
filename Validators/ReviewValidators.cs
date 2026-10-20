using FluentValidation;
using ChessApp.Backend.DTOs;
using ChessApp.Backend.Models;

namespace ChessApp.Backend.Validators;

public class CreateReviewRequestValidator : AbstractValidator<CreateReviewRequest>
{
    public CreateReviewRequestValidator()
    {
        RuleFor(x => x.GatheringId).NotEmpty();
        RuleFor(x => x.RevieweeId).NotEmpty();
        RuleForEach(x => x.Tags)
            .Must(tag => ReviewTags.All.Contains(tag))
            .WithMessage($"Tags must be one of: {string.Join(", ", ReviewTags.All)}");
        RuleFor(x => x.Tags)
            .Empty()
            .When(x => x.NoShow)
            .WithMessage("A no-show review can't carry tags");
        RuleFor(x => x.Comment).MaximumLength(1000);
        RuleFor(x => x)
            .Must(x => x.NoShow || x.Tags.Count > 0 || !string.IsNullOrWhiteSpace(x.Comment))
            .WithName("Tags")
            .WithMessage("Add at least one tag, a comment, or mark them as a no-show");
    }
}
