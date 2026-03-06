namespace FinCore.SharedKernel.Domain.ValueObjects;

public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string CurrencyCode { get; }

    public Money(decimal amount, string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Length != 3)
            throw new ArgumentException("Currency code must be a 3-letter ISO 4217 code.", nameof(currencyCode));

        Amount = amount;
        CurrencyCode = currencyCode.ToUpperInvariant();
    }

    public static Money operator +(Money a, Money b)
    {
        if (a.CurrencyCode != b.CurrencyCode)
            throw new InvalidOperationException($"Cannot add amounts with different currencies: {a.CurrencyCode} and {b.CurrencyCode}.");
        return new Money(a.Amount + b.Amount, a.CurrencyCode);
    }

    public static Money operator -(Money a, Money b)
    {
        if (a.CurrencyCode != b.CurrencyCode)
            throw new InvalidOperationException($"Cannot subtract amounts with different currencies: {a.CurrencyCode} and {b.CurrencyCode}.");
        return new Money(a.Amount - b.Amount, a.CurrencyCode);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return CurrencyCode;
    }

    public override string ToString() => $"{Amount:F2} {CurrencyCode}";
}
