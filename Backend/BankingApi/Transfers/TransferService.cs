using BankingApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Data;

namespace BankingApi.Transfers;

public class TransferService
{
    private const int MaxAttempts = 3;

    private readonly AppDbContext _db;
    private readonly ILogger<TransferService> _logger;

    public TransferService(AppDbContext db, ILogger<TransferService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TransferResult> ExecuteTransferAsync(
        Guid userId, Guid fromAccountId, Guid toAccountId, decimal amount)
    {
        if (amount <= 0m)
        {
            return new TransferResult(false, null, "Amount must be greater than zero", 422);
        }

        if (fromAccountId == toAccountId)
        {
            return new TransferResult(false, null, "Cannot transfer to the same account", 422);
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await TryTransferOnceAsync(userId, fromAccountId, toAccountId, amount);
            }
            catch (Exception ex) when (IsRetryableFailure(ex))
            {
                // The transaction was already rolled back by Dispose in TryTransferOnceAsync.
                _logger.LogWarning(ex,
                    "Transfer {From} -> {To} conflicted (attempt {Attempt}/{Max})",
                    fromAccountId, toAccountId, attempt, MaxAttempts);

                // Discard stale tracked entities before retrying.
                _db.ChangeTracker.Clear();

                if (attempt == MaxAttempts)
                {
                    return new TransferResult(false, null, "Concurrent transfer conflict, please retry", 409);
                }

                // Small backoff with jitter.
                await Task.Delay(Random.Shared.Next(20, 80) * attempt);
            }
        }

        // Unreachable, but required by the compiler.
        return new TransferResult(false, null, "Concurrent transfer conflict, please retry", 409);
    }

    private async Task<TransferResult> TryTransferOnceAsync(
        Guid userId, Guid fromAccountId, Guid toAccountId, decimal amount)
    {
        // Disposing an uncommitted transaction rolls it back automatically,
        // so early returns and exceptions need no manual RollbackAsync.
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        // Lock both rows in one query, ordered by Id, so concurrent A->B and B->A
        // transfers always acquire locks in the same order (no deadlock).
        var ids = new[] { fromAccountId, toAccountId };
        var accounts = await _db.Accounts
            .FromSqlRaw("SELECT * FROM \"Accounts\" WHERE \"Id\" = ANY({0}) ORDER BY \"Id\" FOR UPDATE", ids)
            .ToListAsync();

        var fromAccount = accounts.FirstOrDefault(a => a.Id == fromAccountId);
        var toAccount = accounts.FirstOrDefault(a => a.Id == toAccountId);

        if (fromAccount == null)
        {
            return new TransferResult(false, null, "Source account not found", 422);
        }

        if (fromAccount.OwnerId != userId)
        {
            return new TransferResult(false, null, "Forbidden", 403);
        }

        if (toAccount == null)
        {
            return new TransferResult(false, null, "Destination account not found", 422);
        }

        if (fromAccount.Balance < amount)
        {
            return new TransferResult(false, null, "Insufficient funds", 422);
        }

        fromAccount.Balance -= amount;
        toAccount.Balance += amount;

        var txn = new Transaction
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccountId,
            ToAccountId = toAccountId,
            Amount = amount,
            Status = "Completed",
            CreatedAt = DateTime.UtcNow
        };

        _db.Transactions.Add(txn);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return new TransferResult(true, txn.Id, null, 200);
    }

    // 40001 = serialization_failure, 40P01 = deadlock_detected
    private static bool IsRetryableFailure(Exception ex)
    {
        if (ex is DbUpdateConcurrencyException)
        {
            return true;
        }

        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: "40001" or "40P01" })
            {
                return true;
            }
        }

        return false;
    }
}

public sealed record TransferResult(bool IsSuccess, Guid? TransactionId, string? Error, int StatusCode);