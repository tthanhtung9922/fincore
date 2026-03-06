using FinCore.Accounts.Domain.Repositories;
using FinCore.EventBus.Abstractions;
using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Accounts.Application.Commands.Withdraw;

public class WithdrawCommandHandler : IRequestHandler<WithdrawCommand, Result>
{
    private readonly IAccountRepository _repository;
    private readonly IEventBus _eventBus;

    public WithdrawCommandHandler(IAccountRepository repository, IEventBus eventBus)
    {
        _repository = repository;
        _eventBus = eventBus;
    }

    public async Task<Result> Handle(WithdrawCommand request, CancellationToken cancellationToken)
    {
        var account = await _repository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account is null)
            return Result.Failure("Account not found.");

        account.Withdraw(request.Amount, request.Reference);
        await _repository.UpdateAsync(account, cancellationToken);
        account.ClearDomainEvents();

        return Result.Success();
    }
}
