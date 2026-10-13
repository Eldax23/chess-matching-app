using FluentValidation;
using ChessApp.Backend.DTOs;

namespace ChessApp.Backend.Validators;

public class LichessCallbackRequestValidator : AbstractValidator<LichessCallbackRequest>
{
    public LichessCallbackRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(512);
        RuleFor(x => x.State).NotEmpty().MaximumLength(128);
    }
}

public class LinkChessComRequestValidator : AbstractValidator<LinkChessComRequest>
{
    public LinkChessComRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .Matches("^[A-Za-z0-9_-]{3,25}$")
            .WithMessage("Enter a valid chess.com username");
    }
}
