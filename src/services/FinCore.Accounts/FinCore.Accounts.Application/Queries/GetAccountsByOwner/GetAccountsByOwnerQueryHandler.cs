using FinCore.Accounts.Application.DTOs;
using FinCore.Accounts.Application.Repositories;
using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Accounts.Application.Queries.GetAccountsByOwner;

public class GetAccountsByOwnerQueryHandler : IRequestHandler<GetAccountsByOwnerQuery, Result<PagedList<AccountSummaryDto>>>
{
    private readonly IAccountReadRepository _readRepository;

    public GetAccountsByOwnerQueryHandler(IAccountReadRepository readRepository)
    {
        _readRepository = readRepository;
    }

    public async Task<Result<PagedList<AccountSummaryDto>>> Handle(GetAccountsByOwnerQuery request, CancellationToken cancellationToken)
    {
        var result = await _readRepository.GetByOwnerAsync(request.OwnerId, request.PageNumber, request.PageSize, cancellationToken);
        return Result.Success(result);
    }
}
