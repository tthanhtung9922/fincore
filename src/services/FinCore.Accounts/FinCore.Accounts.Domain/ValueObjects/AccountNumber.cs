using System.Text.RegularExpressions;
using FinCore.SharedKernel.Domain;
using FinCore.SharedKernel.Domain.Exceptions;

namespace FinCore.Accounts.Domain.ValueObjects;

public sealed class AccountNumber : ValueObject
{
    private static readonly Regex AccountNumberPattern = new(@"^ACC-\d{6}-[A-Z0-9]{8}$", RegexOptions.Compiled);

    public string Value { get; }

    public AccountNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("Account number cannot be empty.");

        if (!AccountNumberPattern.IsMatch(value))
            throw new DomainException($"'{value}' is not a valid account number format.");

        Value = value;
    }

    public static AccountNumber Generate()
    {
        var datePart = DateTime.UtcNow.ToString("yyyyMM");
        var randomPart = GenerateRandomChars(8);
        return new AccountNumber($"ACC-{datePart}-{randomPart}");
    }

    private static string GenerateRandomChars(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Range(0, length).Select(_ => chars[random.Next(chars.Length)]).ToArray());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
