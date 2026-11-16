# Issue 01 — Walking Skeleton & Automated Feedback Loop

| Field | Value |
|---|---|
| **Type** | 🤖 AFK Agent |
| **Blocked By** | Nothing — this is the foundation |
| **Tier** | Tier 1 (must) |

## Goal

Stand up the minimum automated feedback loop so that every subsequent issue can be verified end-to-end: `dotnet test` runs and reports, a linter/validator passes, a Dockerfile builds, and there is a local `docker-compose` path that can smoke-test the full app → Postgres → health-endpoint round trip.

> [!IMPORTANT]
> Nothing here ships to AWS yet. The skeleton proves the **local** cycle works before any cloud resource is touched.

---

## Target Files / Modules

```
Backend/
  BankingApi/
    BankingApi.csproj           # ASP.NET Core 10 minimal API project
    Program.cs                  # Host builder + DI wiring
    appsettings.json            # Connection-string placeholder
    appsettings.Development.json
  BankingApi.Tests/
    BankingApi.Tests.csproj     # xUnit + FluentAssertions
    SmokeTests/
      HealthEndpointTests.cs    # GET /health/live → 200
  BankingApi.sln

Dockerfile                      # multi-stage: build → publish → runtime (non-root)
docker-compose.yml              # api + postgres services, health-check on api
.editorconfig                   # C# code style (enforced by build)
Makefile                        # targets: test, lint, build-image, up, down
.gitignore                      # update: add bin/ obj/ .env
plans/PRD.md                    # (existing — do not modify)
```

---

## Tasks

1. **Scaffold solution**: `dotnet new sln` + `dotnet new webapi` (minimal hosting model) + `dotnet new xunit` test project. Wire them into the solution.
2. **Health endpoints**: add `/health/live` (always 200) and `/health/ready` (200 when DB reachable) using `IHealthCheck`.
3. **Dockerfile**: multi-stage build; final stage runs as `USER app` (non-root); exposes port 8080.
4. **docker-compose.yml**: `postgres:16-alpine` + the api image; api depends on postgres health-check; passes `ASPNETCORE_ENVIRONMENT=Development` and `ConnectionStrings__Default`.
5. **Makefile targets**:
   - `make test` → `dotnet test --no-build`
   - `make lint` → `dotnet build /p:TreatWarningsAsErrors=true`
   - `make build-image` → `docker build -t banking-api:local .`
   - `make up` / `make down` → `docker compose up/down`
6. **Smoke test**: `HealthEndpointTests.cs` starts the TestServer (using `WebApplicationFactory`) and asserts `GET /health/live` returns 200.
7. **CI stub** (optional for this issue): `.github/workflows/ci.yml` or `Jenkinsfile` stub that runs `make test` on push to `main` — even if the runner is not configured yet, the file must be valid YAML/Groovy.

---

## Definition of Done

- [x] `make test` exits 0; at least one real test (health live check) passes.
- [x] `make lint` exits 0 with zero warnings.
- [x] `make build-image` produces a runnable image; `docker run --rm banking-api:local` starts without errors.
- [x] `make up` starts both services; `curl localhost:8080/health/live` returns HTTP 200.
- [x] `make down` removes all containers and volumes.
- [x] No `bin/`, `obj/`, `.env` or secret is committed (`.gitignore` verified).
- [x] The Dockerfile runs as a non-root user.
