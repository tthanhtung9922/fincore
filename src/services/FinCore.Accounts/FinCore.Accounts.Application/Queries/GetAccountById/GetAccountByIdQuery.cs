using FinCore.Accounts.Application.DTOs;
using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Accounts.Application.Queries.GetAccountById;

public record GetAccountByIdQuery(Guid AccountId) : IRequest<Result<AccountDto>>;
