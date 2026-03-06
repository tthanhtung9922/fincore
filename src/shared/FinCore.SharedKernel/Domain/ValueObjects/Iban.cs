using System.Text.RegularExpressions;

namespace FinCore.SharedKernel.Domain.ValueObjects;

public sealed class Iban : ValueObject
{
    private static readonly Regex IbanPattern = new(@"^[A-Z]{2}\d{2}[A-Z0-9]{1,30}$", RegexOptions.Compiled);

    public string Value { get; }

    public Iban(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("IBAN cannot be empty.", nameof(value));

        var normalized = value.Replace(" ", "").ToUpperInvariant();

        if (!IbanPattern.IsMatch(normalized))
            throw new ArgumentException($"'{value}' is not a valid IBAN format.", nameof(value));

        Value = normalized;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
