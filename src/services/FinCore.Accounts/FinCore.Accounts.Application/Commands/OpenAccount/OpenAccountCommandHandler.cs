using FinCore.Accounts.Domain.Aggregates;
using FinCore.Accounts.Domain.Enums;
using FinCore.Accounts.Domain.Repositories;
using FinCore.EventBus.Abstractions;
using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Accounts.Application.Commands.OpenAccount;

public class OpenAccountCommandHandler : IRequestHandler<OpenAccountCommand, Result<Guid>>
{
    private readonly IAccountRepository _repository;
    private readonly IEventBus _eventBus;

    public OpenAccountCommandHandler(IAccountRepository repository, IEventBus eventBus)
    {
        _repository = repository;
        _eventBus = eventBus;
    }

    public async Task<Result<Guid>> Handle(OpenAccountCommand request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<AccountType>(request.AccountType, true, out var accountType))
            return Result.Failure<Guid>($"Invalid account type: {request.AccountType}");

        var account = Account.Open(request.OwnerId, accountType, request.Currency.ToUpperInvariant());
        await _repository.AddAsync(account, cancellationToken);
        account.ClearDomainEvents();

        return Result.Success(account.Id);
    }
}
