using FinCore.SharedKernel.Domain.ValueObjects;
using FluentAssertions;

namespace FinCore.Accounts.Domain.Tests;

public class MoneyValueObjectTests
{
    [Fact]
    public void Add_SameCurrency_ReturnsCorrectSum()
    {
        var a = new Money(100m, "USD");
        var b = new Money(50m, "USD");

        var result = a + b;

        result.Amount.Should().Be(150m);
        result.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void Add_DifferentCurrencies_ThrowsException()
    {
        var a = new Money(100m, "USD");
        var b = new Money(50m, "EUR");

        var act = () => { var _ = a + b; };

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Money_EqualityByValue()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "USD");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Money_DifferentAmounts_NotEqual()
    {
        var a = new Money(100m, "USD");
        var b = new Money(200m, "USD");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Money_DifferentCurrencies_NotEqual()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "EUR");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Subtract_SameCurrency_ReturnsCorrectDifference()
    {
        var a = new Money(100m, "USD");
        var b = new Money(40m, "USD");

        var result = a - b;

        result.Amount.Should().Be(60m);
    }
}
