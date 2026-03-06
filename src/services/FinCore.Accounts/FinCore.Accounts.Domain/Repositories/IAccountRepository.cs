using FinCore.Accounts.Domain.Aggregates;

namespace FinCore.Accounts.Domain.Repositories;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Account>> GetByOwnerIdAsync(Guid ownerId, CancellationToken ct = default);
    Task AddAsync(Account account, CancellationToken ct = default);
    Task UpdateAsync(Account account, CancellationToken ct = default);
}
