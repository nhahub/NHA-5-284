# Issue 03 — Accounts Slice: Create & List Own Accounts

| Field | Value |
|---|---|
| **Type** | 🤖 AFK Agent |
| **Blocked By** | #02 (JWT auth middleware must be working) |
| **Tier** | Tier 1 (must) |

## Goal

Ship the `Account` entity with its EF migration, `POST /accounts` (open a new account) and `GET /accounts` (list caller's accounts), enforcing the ownership rule: a user sees only their own accounts. Full vertical slice: DB → domain logic → secured endpoints → integration tests.

> [!NOTE]
> Keep `AccountsModule.cs` as the single entry point. Avoid creating a separate `AccountService` and `IAccountRepository` layer unless complexity genuinely demands it — inline the query logic or use a small static helper.

---

## Target Files / Modules

```
Backend/
  BankingApi/
    Accounts/
      AccountsModule.cs         # MapGroup("/accounts"); both endpoints; auth required
      Account.cs                # Entity: Id (Guid), OwnerId (Guid FK→User),
                                #   AccountNumber (string, unique), Balance (decimal),
                                #   Currency (string "EGP"), CreatedAt
      AccountNumberGenerator.cs # Generates a unique 10-digit account number
    Data/
      AppDbContext.cs           # Add Accounts DbSet; configure decimal precision(18,4)
      Migrations/               # AddAccounts migration

  BankingApi.Tests/
    Accounts/
      AccountsIntegrationTests.cs  # register → login → create account → list accounts
```

---

## Tasks

1. **Account entity + migration**: `Id`, `OwnerId` (FK → `Users.Id`, cascade delete), `AccountNumber` (unique), `Balance` (decimal 18,4, default 0), `Currency` (default "EGP"), `CreatedAt`. Run migration.
2. **AccountNumberGenerator**: generate a random 10-digit string; retry up to 5 times if collision (save to DB, catch unique-violation). Keep it internal to the module.
3. **POST /accounts**: requires `[Authorize]`; reads `OwnerId` from JWT `sub` claim; creates account with zero balance; returns 201 `{ accountId, accountNumber }`.
4. **GET /accounts**: requires `[Authorize]`; filters `WHERE OwnerId = currentUser`; returns array `[{ accountId, accountNumber, balance, currency }]`.
5. **Ownership enforcement**: both endpoints extract the user ID from `ClaimsPrincipal` — no request parameter for `userId`.
6. **Integration tests**:
   - Two users each create an account; each user's `GET /accounts` returns only their own.
   - Unauthenticated `GET /accounts` returns 401.

---

## Definition of Done

- [ ] `POST /accounts` (authenticated) returns 201 with a unique account number.
- [ ] `GET /accounts` returns only the caller's accounts — never another user's.
- [ ] Unauthenticated requests to both endpoints return 401.
- [ ] `Balance` column uses decimal type with precision 18, scale 4 (no floating point).
- [ ] EF migration applies cleanly to a fresh DB.
- [ ] Integration tests pass with `make test`.
- [ ] `make lint` exits 0.
