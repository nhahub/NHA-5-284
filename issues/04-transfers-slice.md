# Issue 04 — Transfers Slice: Atomic Money Movement

| Field | Value |
|---|---|
| **Type** | 🤖 AFK Agent |
| **Blocked By** | #03 (Account entity and DB table must exist) |
| **Tier** | Tier 1 (must) |

## Goal

Ship `POST /transfers` and `GET /accounts/{id}/transactions` as one atomic slice: EF migration for the `Transaction` entity, the transfer domain logic (single DB transaction, balance guards, ownership check), the two endpoints, and integration tests that verify money movement and failure cases.

> [!IMPORTANT]
> Money arithmetic must use `decimal` throughout — never `double` or `float`. The debit + credit must happen inside a **single** `IDbContextTransaction` to prevent partial updates.

---

## Target Files / Modules

```
Backend/
  BankingApi/
    Transfers/
      TransfersModule.cs        # POST /transfers; GET /accounts/{id}/transactions
      Transaction.cs            # Entity: Id, FromAccountId, ToAccountId, Amount,
                                #   Status (enum: Pending/Completed/Failed), CreatedAt
      TransferService.cs        # ExecuteTransfer(): validates, debits, credits, records
                                #   — all inside one DbTransaction
    Data/
      AppDbContext.cs           # Add Transactions DbSet
      Migrations/               # AddTransactions migration

  BankingApi.Tests/
    Transfers/
      TransferIntegrationTests.cs  # happy path + insufficient funds + cross-user guard
```

---

## Tasks

1. **Transaction entity + migration**: `Id` (Guid), `FromAccountId` (Guid FK → Accounts), `ToAccountId` (Guid FK → Accounts), `Amount` (decimal 18,4), `Status` (string or enum column), `CreatedAt`. Index on `FromAccountId` and `ToAccountId`.
2. **TransferService.ExecuteTransfer**:
   - Open a serializable DB transaction.
   - Lock and reload both accounts (`WITH (UPDLOCK)` hint or `UseSerializableTransaction`).
   - Guard: caller owns `FromAccount`; `Amount > 0`; `FromAccount.Balance >= Amount`.
   - Debit `FromAccount`, credit `ToAccount`, insert `Transaction` (Status = Completed).
   - Commit. On any guard failure: return a typed error result (no exception-as-flow-control for validation).
3. **POST /transfers** (auth required): body `{ fromAccountId, toAccountId, amount }`; calls `TransferService`; returns 200 `{ transactionId }` or 422 `{ error }`.
4. **GET /accounts/{id}/transactions** (auth required): caller must own account `{id}`; returns all transactions where `FromAccountId = id` OR `ToAccountId = id`, ordered by `CreatedAt DESC`.
5. **Integration tests**:
   - Happy path: balances updated correctly, transaction recorded.
   - Insufficient funds: 422, balances unchanged.
   - Cross-user: caller tries to send from another user's account → 403.
   - Concurrent transfer: two simultaneous transfers from the same account; only one can succeed if it would overdraw.

---

## Definition of Done

- [ ] `POST /transfers` with sufficient funds returns 200; both balances are updated atomically.
- [ ] `POST /transfers` with insufficient funds returns 422; no balance changes persist.
- [ ] Attempting to transfer from another user's account returns 403.
- [ ] `GET /accounts/{id}/transactions` returns both incoming and outgoing transactions; non-owner gets 403.
- [ ] All arithmetic uses `decimal`; no `float` or `double` appears in money-handling code.
- [ ] EF migration applies cleanly.
- [ ] Integration tests (including the concurrent case) pass with `make test`.
