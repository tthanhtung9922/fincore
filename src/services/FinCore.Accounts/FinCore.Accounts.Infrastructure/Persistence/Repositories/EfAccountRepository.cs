using FinCore.Accounts.Domain.Aggregates;
using FinCore.Accounts.Domain.Enums;
using FinCore.Accounts.Domain.Repositories;
using FinCore.Accounts.Domain.ValueObjects;
using FinCore.Accounts.Infrastructure.Persistence.Entities;
using FinCore.SharedKernel.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Accounts.Infrastructure.Persistence.Repositories;

public class EfAccountRepository : IAccountRepository
{
    private readonly AccountsDbContext _context;

    public EfAccountRepository(AccountsDbContext context)
    {
        _context = context;
    }

    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        return entity is null ? null : MapToDomain(entity);
    }

    public async Task<IReadOnlyList<Account>> GetByOwnerIdAsync(Guid ownerId, CancellationToken ct = default)
    {
        var entities = await _context.Accounts
            .Where(a => a.OwnerId == ownerId)
            .ToListAsync(ct);

        return entities.Select(MapToDomain).ToList().AsReadOnly();
    }

    public async Task AddAsync(Account account, CancellationToken ct = default)
    {
        var entity = MapToEntity(account);
        await _context.Accounts.AddAsync(entity, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Account account, CancellationToken ct = default)
    {
        var entity = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == account.Id, ct);
        if (entity is null) return;

        entity.BalanceAmount = account.Balance.Amount;
        entity.BalanceCurrency = account.Balance.CurrencyCode;
        entity.Status = account.Status.ToString();
        entity.Version = account.Version;

        await _context.SaveChangesAsync(ct);
    }

    private static Account MapToDomain(AccountEntity entity)
    {
        // Reconstitute from stored events by replaying the AccountOpened event
        var openedEvent = new FinCore.Accounts.Domain.Events.AccountOpened(
            entity.Id,
            entity.OwnerId,
            entity.AccountType,
            entity.Currency,
            entity.AccountNumber);

        // Use reflection to create and rehydrate the account
        var account = (Account)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Account));

        // Initialize _domainEvents list (skipped by GetUninitializedObject)
        var domainEventsField = typeof(FinCore.SharedKernel.Domain.AggregateRoot)
            .GetField("_domainEvents", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        domainEventsField?.SetValue(account, new List<FinCore.SharedKernel.Domain.DomainEvent>());

        // Directly set properties via reflection
        SetProperty(account, "Id", entity.Id);
        SetProperty(account, "OwnerId", entity.OwnerId);
        SetProperty(account, "AccountNumber", new AccountNumber(entity.AccountNumber));
        SetProperty(account, "AccountType", Enum.Parse<AccountType>(entity.AccountType));
        SetProperty(account, "Balance", new Money(entity.BalanceAmount, entity.BalanceCurrency));
        SetProperty(account, "Currency", entity.Currency);
        SetProperty(account, "Status", Enum.Parse<AccountStatus>(entity.Status));
        SetProperty(account, "CreatedAt", entity.CreatedAt);

        return account;
    }

    private static void SetProperty(object obj, string propertyName, object value)
    {
        var prop = obj.GetType().GetProperty(propertyName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (prop is null)
        {
            // Try base classes
            var type = obj.GetType().BaseType;
            while (type is not null)
            {
                prop = type.GetProperty(propertyName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop is not null) break;
                type = type.BaseType;
            }
        }

        prop?.SetValue(obj, value);
    }

    private static AccountEntity MapToEntity(Account account) => new()
    {
        Id = account.Id,
        OwnerId = account.OwnerId,
        AccountNumber = account.AccountNumber.Value,
        AccountType = account.AccountType.ToString(),
        BalanceAmount = account.Balance.Amount,
        BalanceCurrency = account.Balance.CurrencyCode,
        Currency = account.Currency,
        Status = account.Status.ToString(),
        CreatedAt = account.CreatedAt,
        Version = account.Version
    };
}
