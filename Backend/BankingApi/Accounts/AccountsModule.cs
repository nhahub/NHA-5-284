using BankingApi.Auth;
using BankingApi.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BankingApi.Accounts;

public static class AccountsModule
{
    public static IEndpointRouteBuilder MapAccountsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/accounts").RequireAuthorization();

        group.MapPost("/", CreateAccount)
             .WithName("CreateAccount");

        group.MapGet("/", GetUserAccounts)
             .WithName("GetUserAccounts");

        return endpoints;
    }

    private static async Task<IResult> CreateAccount(ClaimsPrincipal principal, AppDbContext db)
    {
        var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(userIdStr, out var ownerId))
        {
            return Results.Unauthorized();
        }

        var accountNumber = await AccountNumberGenerator.GenerateUniqueAsync(db);

        var account = new Account
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            AccountNumber = accountNumber,
            Balance = 0m,
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return Results.Created($"/accounts/{account.Id}", new
        {
            accountId = account.Id,
            accountNumber = account.AccountNumber
        });
    }

    private static async Task<IResult> GetUserAccounts(ClaimsPrincipal principal, AppDbContext db)
    {
        var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(userIdStr, out var ownerId))
        {
            return Results.Unauthorized();
        }

        var accounts = await db.Accounts
            .Where(a => a.OwnerId == ownerId)
            .Select(a => new
            {
                accountId = a.Id,
                accountNumber = a.AccountNumber,
                balance = a.Balance,
                currency = a.Currency,
                createdAt = a.CreatedAt
            })
            .ToListAsync();

        return Results.Ok(accounts);
    }
}