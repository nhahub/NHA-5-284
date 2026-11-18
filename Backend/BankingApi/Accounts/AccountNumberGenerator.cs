using System.Globalization;
using BankingApi.Data;
using Microsoft.EntityFrameworkCore;

namespace BankingApi.Accounts;

public static class AccountNumberGenerator
{
    public static async Task<string> GenerateUniqueAsync(AppDbContext db)
    {
        for (int i = 0; i < 5; i++)
        {
            var number = Random.Shared.NextInt64(1000000000L, 10000000000L).ToString(CultureInfo.InvariantCulture);
            var exists = await db.Accounts.AnyAsync(a => a.AccountNumber == number);
            if (!exists)
            {
                return number;
            }
        }
        return DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)[^10..];
    }
}
