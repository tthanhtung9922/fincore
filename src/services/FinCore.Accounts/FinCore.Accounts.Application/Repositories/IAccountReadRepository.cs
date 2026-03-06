using FinCore.Accounts.Application.DTOs;
using FinCore.SharedKernel.Common;

namespace FinCore.Accounts.Application.Repositories;

public interface IAccountReadRepository
{
    Task<AccountDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedList<AccountSummaryDto>> GetByOwnerAsync(Guid ownerId, int pageNumber, int pageSize, CancellationToken ct = default);
}
