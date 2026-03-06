namespace FinCore.Accounts.Infrastructure.Persistence.Entities;

public class AccountEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public decimal BalanceAmount { get; set; }
    public string BalanceCurrency { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
}
