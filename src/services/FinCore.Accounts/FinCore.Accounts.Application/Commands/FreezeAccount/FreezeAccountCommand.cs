using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Accounts.Application.Commands.FreezeAccount;

public record FreezeAccountCommand(Guid AccountId, string Reason) : IRequest<Result>;
