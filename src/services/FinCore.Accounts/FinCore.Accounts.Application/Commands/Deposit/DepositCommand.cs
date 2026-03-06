using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Accounts.Application.Commands.Deposit;

public record DepositCommand(Guid AccountId, decimal Amount, string Currency, string Reference) : IRequest<Result>;
