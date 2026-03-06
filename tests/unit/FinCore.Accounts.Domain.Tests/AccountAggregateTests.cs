using FinCore.Accounts.Domain.Aggregates;
using FinCore.Accounts.Domain.Enums;
using FinCore.Accounts.Domain.Events;
using FinCore.Accounts.Domain.Exceptions;
using FinCore.SharedKernel.Domain.Exceptions;
using FluentAssertions;

namespace FinCore.Accounts.Domain.Tests;

public class AccountAggregateTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    [Fact]
    public void Open_WithValidData_RaisesAccountOpenedEvent()
    {
        var account = Account.Open(OwnerId, AccountType.Checking, "USD");

        account.DomainEvents.Should().HaveCount(1);
        account.DomainEvents[0].Should().BeOfType<AccountOpened>();

        var evt = (AccountOpened)account.DomainEvents[0];
        evt.OwnerId.Should().Be(OwnerId);
        evt.Currency.Should().Be("USD");
        evt.AccountType.Should().Be(AccountType.Checking.ToString());
    }

    [Fact]
    public void Open_WithValidData_SetsInitialState()
    {
        var account = Account.Open(OwnerId, AccountType.Savings, "EUR");

        account.Status.Should().Be(AccountStatus.Active);
        account.Balance.Amount.Should().Be(0);
        account.Balance.CurrencyCode.Should().Be("EUR");
        account.OwnerId.Should().Be(OwnerId);
    }

    [Fact]
    public void Deposit_PositiveAmount_IncreasesBalance()
    {
        var account = Account.Open(OwnerId, AccountType.Checking, "USD");
        account.ClearDomainEvents();

        account.Deposit(100m, "REF-001");

        account.Balance.Amount.Should().Be(100m);
        account.DomainEvents.Should().HaveCount(1);
        account.DomainEvents[0].Should().BeOfType<MoneyDeposited>();
    }

    [Fact]
    public void Withdraw_SufficientFunds_DecreasesBalance()
    {
        var account = Account.Open(OwnerId, AccountType.Checking, "USD");
        account.Deposit(500m, "REF-001");
        account.ClearDomainEvents();

        account.Withdraw(200m, "REF-002");

        account.Balance.Amount.Should().Be(300m);
        account.DomainEvents.Should().HaveCount(1);
        account.DomainEvents[0].Should().BeOfType<MoneyWithdrawn>();
    }

    [Fact]
    public void Withdraw_InsufficientFunds_ThrowsInsufficientFundsException()
    {
        var account = Account.Open(OwnerId, AccountType.Checking, "USD");
        account.Deposit(100m, "REF-001");

        var act = () => account.Withdraw(200m, "REF-002");

        act.Should().Throw<InsufficientFundsException>();
    }

    [Fact]
    public void Withdraw_FrozenAccount_ThrowsAccountNotActiveException()
    {
        var account = Account.Open(OwnerId, AccountType.Checking, "USD");
        account.Deposit(500m, "REF-001");
        account.Freeze("Suspicious activity");

        var act = () => account.Withdraw(100m, "REF-002");

        act.Should().Throw<AccountNotActiveException>();
    }

    [Fact]
    public void Close_NonZeroBalance_ThrowsDomainException()
    {
        var account = Account.Open(OwnerId, AccountType.Checking, "USD");
        account.Deposit(100m, "REF-001");

        var act = () => account.Close();

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Close_ZeroBalance_RaisesAccountClosedEvent()
    {
        var account = Account.Open(OwnerId, AccountType.Checking, "USD");
        account.ClearDomainEvents();

        account.Close();

        account.Status.Should().Be(AccountStatus.Closed);
        account.DomainEvents.Should().HaveCount(1);
        account.DomainEvents[0].Should().BeOfType<AccountClosed>();
    }

    [Fact]
    public void Freeze_ActiveAccount_ChangesStatusToFrozen()
    {
        var account = Account.Open(OwnerId, AccountType.Checking, "USD");
        account.ClearDomainEvents();

        account.Freeze("Compliance review");

        account.Status.Should().Be(AccountStatus.Frozen);
        account.DomainEvents[0].Should().BeOfType<AccountFrozen>();
    }

    [Fact]
    public void Unfreeze_FrozenAccount_ChangesStatusToActive()
    {
        var account = Account.Open(OwnerId, AccountType.Checking, "USD");
        account.Freeze("Reason");
        account.ClearDomainEvents();

        account.Unfreeze();

        account.Status.Should().Be(AccountStatus.Active);
        account.DomainEvents[0].Should().BeOfType<AccountUnfrozen>();
    }
}
