namespace FinCore.Accounts.Application.DTOs;

public record AccountSummaryDto(
    Guid Id,
    string AccountNumber,
    string AccountType,
    decimal Balance,
    string Currency,
    string Status);
