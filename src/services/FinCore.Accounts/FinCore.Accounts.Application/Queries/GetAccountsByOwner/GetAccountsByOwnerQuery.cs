using FinCore.Accounts.Application.DTOs;
using FinCore.SharedKernel.Common;
using MediatR;

namespace FinCore.Accounts.Application.Queries.GetAccountsByOwner;

public record GetAccountsByOwnerQuery(Guid OwnerId, int PageNumber = 1, int PageSize = 20) : IRequest<Result<PagedList<AccountSummaryDto>>>;
