using FluentValidation;
using ChessApp.Backend.DTOs;

namespace ChessApp.Backend.Validators;

public class CreateGatheringRequestValidator : AbstractValidator<CreateGatheringRequest>
{
    public CreateGatheringRequestValidator()
    {
        RuleFor(x => x.VenueName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Note).MaximumLength(500);

        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);

        RuleFor(x => x.EndsAt)
            .GreaterThan(x => x.StartsAt)
            .WithMessage("The gathering must end after it starts");
        RuleFor(x => x.EndsAt)
            .GreaterThan(_ => DateTime.UtcNow)
            .WithMessage("The gathering must end in the future");
        RuleFor(x => x)
            .Must(x => x.EndsAt - x.StartsAt <= TimeSpan.FromHours(12))
            .WithName("EndsAt")
            .WithMessage("A gathering can last at most 12 hours");
        RuleFor(x => x.StartsAt)
            .LessThan(_ => DateTime.UtcNow.AddDays(14))
            .WithMessage("A gathering can start at most 14 days from now");
    }
}

public class JoinGatheringRequestValidator : AbstractValidator<JoinGatheringRequest>
{
    public JoinGatheringRequestValidator()
    {
        RuleFor(x => x.Message).MaximumLength(500);
    }
}
