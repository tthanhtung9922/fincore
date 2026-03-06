using FinCore.Identity.Domain.Enums;
using FluentValidation;

namespace FinCore.Identity.Application.Commands.RegisterUser;

public class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(12)
            .WithMessage("Password must be at least 12 characters.");

        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(r => Enum.TryParse<UserRole>(r, true, out _))
            .WithMessage("Role must be one of: Customer, Analyst, ComplianceOfficer, Admin.");
    }
}
