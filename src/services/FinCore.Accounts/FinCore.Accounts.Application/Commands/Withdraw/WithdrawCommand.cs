using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Accounts.Application.Commands.Withdraw;

public record WithdrawCommand(Guid AccountId, decimal Amount, string Currency, string Reference) : IRequest<Result>;
