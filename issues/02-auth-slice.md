# Issue 02 — Auth Slice: Register, Login & JWT Middleware

| Field | Value |
|---|---|
| **Type** | 🤖 AFK Agent |
| **Blocked By** | #01 (walking skeleton, working DB connection) |
| **Tier** | Tier 1 (must) |

## Goal

Ship the complete `POST /auth/register` → `POST /auth/login` → JWT bearer vertical slice, including the EF Core migration, the domain logic (password hashing, token generation), the API endpoints, and an integration test that hits a real PostgreSQL container.

> [!NOTE]
> Keep the public surface narrow: one `AuthModule` class wires both endpoints and owns the `User` entity, `PasswordHasher`, and `TokenService`. Do **not** create a separate `IUserRepository` wrapper just to mock it — the integration test uses a real DB.

---

## Target Files / Modules

```
Backend/
  BankingApi/
    Auth/
      AuthModule.cs             # MapGroup("/auth"), registers both endpoints inline
      User.cs                   # Entity: Id, Email, PasswordHash, Role, CreatedAt
      TokenService.cs           # GenerateJwt(User) → signed JWT (HS256)
      PasswordHasher.cs         # BCrypt wrapper (Bcrypt.Net-Next)
    Data/
      AppDbContext.cs           # EF Core DbContext; Users DbSet
      Migrations/               # EF-generated initial migration (Users table)
    appsettings.json            # Jwt:Key, Jwt:Issuer, Jwt:Audience (env-override)
    Program.cs                  # AddAuthentication/AddJwtBearer; AuthModule.Map()

  BankingApi.Tests/
    Auth/
      AuthIntegrationTests.cs   # register → login → verify token round-trip
                                # Uses WebApplicationFactory + real Postgres via
                                # Testcontainers-dotnet (Testcontainers.PostgreSql)
```

---

## Tasks

1. **User entity + EF migration**: `User` with `Id` (Guid), `Email` (unique index), `PasswordHash`, `Role` (string "Customer"), `CreatedAt`. Run `dotnet ef migrations add InitUsers`.
2. **Password hashing**: thin static-ish wrapper around `BCrypt.Net.BCrypt`; no interface needed.
3. **TokenService**: reads `Jwt:*` config, returns a signed `JwtSecurityToken` with `sub`, `email`, `role` and `exp` claims.
4. **AuthModule endpoints**:
   - `POST /auth/register`: validate email + password (non-empty, min 8 chars), hash, insert, return 201 `{ userId }`.
   - `POST /auth/login`: look up by email, verify hash, return 200 `{ token, expiresAt }`. Return 401 on bad credentials (same message for both cases — no user enumeration).
5. **JWT middleware**: `AddAuthentication(JwtBearer)` wired in `Program.cs`; a `[Authorize]` attribute on a test endpoint proves it blocks 401.
6. **Integration test**: `AuthIntegrationTests` spins up a Postgres container via Testcontainers, registers a user, logs in, decodes the JWT, asserts claims.
7. **`make test`** must still pass end-to-end.

---

## Definition of Done

- [ ] `POST /auth/register` with valid body returns 201; duplicate email returns 409.
- [ ] `POST /auth/login` with correct credentials returns a valid JWT; wrong password returns 401.
- [ ] The JWT contains correct `sub`, `email`, `role` claims and an `exp` in the future.
- [ ] A protected endpoint returns 401 without a token and 200 with a valid token.
- [ ] EF migration applies cleanly with `dotnet ef database update` against a fresh DB.
- [ ] Integration test passes in CI (`make test`).
- [ ] No plaintext passwords or JWT secrets committed; `Jwt:Key` is read from env / user-secrets.
