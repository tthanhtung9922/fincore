using FinCore.Accounts.Domain.Enums;
using FluentValidation;

namespace FinCore.Accounts.Application.Commands.OpenAccount;

public class OpenAccountCommandValidator : AbstractValidator<OpenAccountCommand>
{
    public OpenAccountCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();

        RuleFor(x => x.AccountType)
            .NotEmpty()
            .Must(t => Enum.TryParse<AccountType>(t, true, out _))
            .WithMessage("AccountType must be one of: Checking, Savings, Investment.");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3)
            .WithMessage("Currency must be a 3-letter ISO 4217 code.");
    }
}
