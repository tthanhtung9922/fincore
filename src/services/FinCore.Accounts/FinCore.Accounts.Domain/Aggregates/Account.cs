using FinCore.Accounts.Domain.Enums;
using FinCore.Accounts.Domain.Events;
using FinCore.Accounts.Domain.Exceptions;
using FinCore.Accounts.Domain.ValueObjects;
using FinCore.SharedKernel.Domain;
using FinCore.SharedKernel.Domain.Exceptions;
using FinCore.SharedKernel.Domain.ValueObjects;

namespace FinCore.Accounts.Domain.Aggregates;

public class Account : AggregateRoot
{
    public Guid OwnerId { get; private set; }
    public AccountNumber AccountNumber { get; private set; } = null!;
    public AccountType AccountType { get; private set; }
    public Money Balance { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public AccountStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Account() { } // EF constructor

    public static Account Open(Guid ownerId, AccountType accountType, string currency)
    {
        var account = new Account();
        var accountNumber = AccountNumber.Generate();

        account.RaiseEvent(new AccountOpened(
            Guid.NewGuid(),
            ownerId,
            accountType.ToString(),
            currency,
            accountNumber.Value));

        return account;
    }

    public void Deposit(decimal amount, string reference)
    {
        if (Status != AccountStatus.Active)
            throw new AccountNotActiveException(Id, Status.ToString());

        if (amount <= 0)
            throw new DomainException("Deposit amount must be positive.");

        RaiseEvent(new MoneyDeposited(Id, amount, Currency, reference));
    }

    public void Withdraw(decimal amount, string reference)
    {
        if (Status != AccountStatus.Active)
            throw new AccountNotActiveException(Id, Status.ToString());

        if (amount <= 0)
            throw new DomainException("Withdrawal amount must be positive.");

        if (Balance.Amount < amount)
            throw new InsufficientFundsException(amount, Balance.Amount, Currency);

        RaiseEvent(new MoneyWithdrawn(Id, amount, Currency, reference));
    }

    public void Freeze(string reason)
    {
        if (Status == AccountStatus.Closed)
            throw new AccountAlreadyClosedException(Id);

        if (Status == AccountStatus.Frozen)
            throw new DomainException($"Account '{Id}' is already frozen.");

        RaiseEvent(new AccountFrozen(Id, reason));
    }

    public void Unfreeze()
    {
        if (Status != AccountStatus.Frozen)
            throw new DomainException($"Account '{Id}' is not frozen.");

        RaiseEvent(new AccountUnfrozen(Id));
    }

    public void Close()
    {
        if (Status == AccountStatus.Closed)
            throw new AccountAlreadyClosedException(Id);

        if (Balance.Amount != 0)
            throw new DomainException($"Cannot close account '{Id}' with non-zero balance ({Balance}).");

        RaiseEvent(new AccountClosed(Id, DateTimeOffset.UtcNow));
    }

    protected override void Apply(DomainEvent @event)
    {
        switch (@event)
        {
            case AccountOpened opened:
                Id = opened.AccountId;
                OwnerId = opened.OwnerId;
                AccountNumber = new AccountNumber(opened.AccountNumber);
                AccountType = Enum.Parse<AccountType>(opened.AccountType);
                Currency = opened.Currency;
                Balance = new Money(0, opened.Currency);
                Status = AccountStatus.Active;
                CreatedAt = opened.OccurredAt;
                break;

            case MoneyDeposited deposited:
                Balance = Balance + new Money(deposited.Amount, deposited.Currency);
                break;

            case MoneyWithdrawn withdrawn:
                Balance = Balance - new Money(withdrawn.Amount, withdrawn.Currency);
                break;

            case AccountFrozen:
                Status = AccountStatus.Frozen;
                break;

            case AccountUnfrozen:
                Status = AccountStatus.Active;
                break;

            case AccountClosed:
                Status = AccountStatus.Closed;
                break;
        }
    }
}
