namespace FinCore.Accounts.Application.DTOs;

public record AccountDto(
    Guid Id,
    Guid OwnerId,
    string AccountNumber,
    string AccountType,
    decimal Balance,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt);
