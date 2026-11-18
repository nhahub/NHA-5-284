namespace BankingApi.Accounts;

public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public decimal Balance { get; set; } = 0m;
    public string Currency { get; set; } = "EGP";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
