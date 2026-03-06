using FinCore.Accounts.Application.DTOs;
using FinCore.Accounts.Application.Repositories;
using FinCore.Accounts.Infrastructure.Persistence;
using FinCore.SharedKernel.Common;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Accounts.Infrastructure.Persistence.ReadModels;

public class AccountReadRepository : IAccountReadRepository
{
    private readonly AccountsDbContext _context;

    public AccountReadRepository(AccountsDbContext context)
    {
        _context = context;
    }

    public async Task<AccountDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Accounts
            .Where(a => a.Id == id)
            .Select(a => new AccountDto(
                a.Id,
                a.OwnerId,
                a.AccountNumber,
                a.AccountType,
                a.BalanceAmount,
                a.Currency,
                a.Status,
                a.CreatedAt))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PagedList<AccountSummaryDto>> GetByOwnerAsync(Guid ownerId, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var query = _context.Accounts.Where(a => a.OwnerId == ownerId);
        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AccountSummaryDto(
                a.Id,
                a.AccountNumber,
                a.AccountType,
                a.BalanceAmount,
                a.Currency,
                a.Status))
            .ToListAsync(ct);

        return new PagedList<AccountSummaryDto>(items.AsReadOnly(), totalCount, pageNumber, pageSize);
    }
}
