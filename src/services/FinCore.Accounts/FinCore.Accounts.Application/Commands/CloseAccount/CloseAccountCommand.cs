using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Accounts.Application.Commands.CloseAccount;

public record CloseAccountCommand(Guid AccountId) : IRequest<Result>;
