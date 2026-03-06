using FinCore.Accounts.Domain.Repositories;
using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Accounts.Application.Commands.FreezeAccount;

public class FreezeAccountCommandHandler : IRequestHandler<FreezeAccountCommand, Result>
{
    private readonly IAccountRepository _repository;

    public FreezeAccountCommandHandler(IAccountRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result> Handle(FreezeAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await _repository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account is null)
            return Result.Failure("Account not found.");

        account.Freeze(request.Reason);
        await _repository.UpdateAsync(account, cancellationToken);
        account.ClearDomainEvents();

        return Result.Success();
    }
}
