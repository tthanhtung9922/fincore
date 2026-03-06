using FinCore.Accounts.Application.DTOs;
using FinCore.Accounts.Application.Repositories;
using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Accounts.Application.Queries.GetAccountById;

public class GetAccountByIdQueryHandler : IRequestHandler<GetAccountByIdQuery, Result<AccountDto>>
{
    private readonly IAccountReadRepository _readRepository;

    public GetAccountByIdQueryHandler(IAccountReadRepository readRepository)
    {
        _readRepository = readRepository;
    }

    public async Task<Result<AccountDto>> Handle(GetAccountByIdQuery request, CancellationToken cancellationToken)
    {
        var account = await _readRepository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account is null)
            return Result.Failure<AccountDto>("Account not found.");

        return Result.Success(account);
    }
}
