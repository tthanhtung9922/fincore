using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Accounts.Application.Commands.OpenAccount;

public record OpenAccountCommand(Guid OwnerId, string AccountType, string Currency) : IRequest<Result<Guid>>;
