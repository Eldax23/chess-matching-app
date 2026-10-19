using FluentValidation;
using ChessApp.Backend.DTOs;

namespace ChessApp.Backend.Validators;

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FullName).MaximumLength(255);
        RuleFor(x => x.Bio).MaximumLength(2000);
        RuleFor(x => x.FideId).MaximumLength(20);
        RuleFor(x => x.FideRating).InclusiveBetween(0, 4000)
            .When(x => x.FideRating.HasValue);
    }
}
