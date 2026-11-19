using BankingApi.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BankingApi.Transfers;

public static class TransfersModule
{
    public static IEndpointRouteBuilder MapTransfersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/transfers", CreateTransfer)
             .RequireAuthorization()
             .WithName("CreateTransfer");

        endpoints.MapGet("/accounts/{id}/transactions", GetAccountTransactions)
             .RequireAuthorization()
             .WithName("GetAccountTransactions");

        return endpoints;
    }

    private static async Task<IResult> CreateTransfer(
        ClaimsPrincipal principal,
        TransferRequest request,
        TransferService transferService)
    {
        var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await transferService.ExecuteTransferAsync(userId, request.FromAccountId, request.ToAccountId, request.Amount);

        if (!result.IsSuccess)
        {
            return result.StatusCode switch
            {
                403 => Results.Forbid(),
                422 => Results.UnprocessableEntity(new { error = result.Error }),
                _ => Results.BadRequest(new { error = result.Error })
            };
        }

        return Results.Ok(new { transactionId = result.TransactionId });
    }

    private static async Task<IResult> GetAccountTransactions(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db)
    {
        var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return Results.Unauthorized();
        }

        var account = await db.Accounts.FindAsync(id);
        if (account == null)
        {
            return Results.NotFound(new { error = "Account not found" });
        }

        if (account.OwnerId != userId)
        {
            return Results.Forbid();
        }

        var transactions = await db.Transactions
            .Where(t => t.FromAccountId == id || t.ToAccountId == id)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                id = t.Id,
                fromAccountId = t.FromAccountId,
                toAccountId = t.ToAccountId,
                amount = t.Amount,
                status = t.Status,
                createdAt = t.CreatedAt
            })
            .ToListAsync();

        return Results.Ok(transactions);
    }
}

public sealed record TransferRequest(Guid FromAccountId, Guid ToAccountId, decimal Amount);
