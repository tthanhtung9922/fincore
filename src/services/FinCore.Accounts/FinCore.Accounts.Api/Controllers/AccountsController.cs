using FinCore.Accounts.Application.Commands.CloseAccount;
using FinCore.Accounts.Application.Commands.Deposit;
using FinCore.Accounts.Application.Commands.FreezeAccount;
using FinCore.Accounts.Application.Commands.OpenAccount;
using FinCore.Accounts.Application.Commands.Withdraw;
using FinCore.Accounts.Application.Queries.GetAccountById;
using FinCore.Accounts.Application.Queries.GetAccountsByOwner;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FinCore.Accounts.Api.Controllers;

[ApiController]
[Route("api/v1/accounts")]
[Authorize]
public class AccountsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AccountsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> OpenAccount([FromBody] OpenAccountRequest request, CancellationToken ct)
    {
        var ownerId = GetCurrentUserId();
        if (ownerId is null) return Unauthorized();

        var result = await _mediator.Send(
            new OpenAccountCommand(ownerId.Value, request.AccountType, request.Currency), ct);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(GetById), new { id = result.Value }, new { id = result.Value });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAccountByIdQuery(id), ct);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });

        var currentUserId = GetCurrentUserId();
        var isPrivileged = User.IsInRole("Admin") || User.IsInRole("Analyst");

        if (!isPrivileged && result.Value.OwnerId != currentUserId)
            return Forbid();

        return Ok(result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var ownerId = GetCurrentUserId();
        if (ownerId is null) return Unauthorized();

        var queryOwnerId = User.IsInRole("Admin") || User.IsInRole("Analyst")
            ? (Guid?)null
            : ownerId.Value;

        var result = await _mediator.Send(new GetAccountsByOwnerQuery(queryOwnerId ?? ownerId.Value, pageNumber, pageSize), ct);
        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/deposit")]
    public async Task<IActionResult> Deposit(Guid id, [FromBody] MoneyOperationRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new DepositCommand(id, request.Amount, request.Currency, request.Reference), ct);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok();
    }

    [HttpPost("{id:guid}/withdraw")]
    public async Task<IActionResult> Withdraw(Guid id, [FromBody] MoneyOperationRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new WithdrawCommand(id, request.Amount, request.Currency, request.Reference), ct);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok();
    }

    [HttpPost("{id:guid}/freeze")]
    [Authorize(Roles = "ComplianceOfficer,Admin")]
    public async Task<IActionResult> Freeze(Guid id, [FromBody] FreezeRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new FreezeAccountCommand(id, request.Reason), ct);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new CloseAccountCommand(id), ct);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return NoContent();
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

public record OpenAccountRequest(string AccountType, string Currency);
public record MoneyOperationRequest(decimal Amount, string Currency, string Reference);
public record FreezeRequest(string Reason);
