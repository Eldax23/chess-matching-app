using FluentValidation;
using ChessApp.Backend.DTOs;

namespace ChessApp.Backend.Validators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(255);

        RuleFor(x => x.Username)
            .NotEmpty()
            .Length(3, 20)
            .Matches("^[a-zA-Z0-9_]+$")
            .WithMessage("Username may only contain letters, numbers, and underscores");

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters");

        RuleFor(x => x.FullName)
            .MaximumLength(255)
            .When(x => x.FullName != null);
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .Matches(@"^\s*\d{6}\s*$")
            .WithMessage("Enter the 6-digit code from the email");
    }
}
