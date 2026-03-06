using FinCore.Accounts.Domain.Repositories;
using FinCore.EventBus.Abstractions;
using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Accounts.Application.Commands.Deposit;

public class DepositCommandHandler : IRequestHandler<DepositCommand, Result>
{
    private readonly IAccountRepository _repository;
    private readonly IEventBus _eventBus;

    public DepositCommandHandler(IAccountRepository repository, IEventBus eventBus)
    {
        _repository = repository;
        _eventBus = eventBus;
    }

    public async Task<Result> Handle(DepositCommand request, CancellationToken cancellationToken)
    {
        var account = await _repository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account is null)
            return Result.Failure("Account not found.");

        account.Deposit(request.Amount, request.Reference);
        await _repository.UpdateAsync(account, cancellationToken);
        account.ClearDomainEvents();

        return Result.Success();
    }
}
