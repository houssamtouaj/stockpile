# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Stockpile is a multi-warehouse inventory and order backend (.NET 10 / C# 14, Minimal APIs, EF Core 10 + Npgsql, PostgreSQL 16, MediatR, FluentValidation, xUnit v3 + Shouldly + NSubstitute + Testcontainers). Stock quantities are a snapshot derived from an append-only movement ledger, and every stock mutation has to stay correct under concurrency.

## Commands

```bash
# Dev dependencies. Postgres is published on 5433, NOT 5432 (dev connection string expects 5433).
docker compose -f docker-compose.dev.yml up -d

# Apply migrations / add one (needs: dotnet tool install --global dotnet-ef)
dotnet ef database update -p src/Stockpile.Infrastructure -s src/Stockpile.Api
dotnet ef migrations add <Name> -p src/Stockpile.Infrastructure -s src/Stockpile.Api -o Persistence/Migrations

dotnet build                                  # TreatWarningsAsErrors + EnforceCodeStyleInBuild are on
dotnet run --project src/Stockpile.Api        # OpenAPI at /openapi/v1.json, Scalar UI at /scalar

# Tests (CI runs them in this order: architecture first, because a layering break muddies everything else)
dotnet test tests/Stockpile.Architecture.Tests
dotnet test tests/Stockpile.Domain.UnitTests
dotnet test tests/Stockpile.Application.UnitTests
dotnet test tests/Stockpile.Api.IntegrationTests     # needs a running Docker daemon

# Single test / class
dotnet test tests/Stockpile.Api.IntegrationTests --filter "FullyQualifiedName~SalesOrderTests.Cancel_whenConfirmed"
```

Package versions are centrally managed in `Directory.Packages.props`; `.csproj` files reference packages without versions.

## Planning docs and branching

- `docs/plans/backend/00-overview.md` holds the global constraints, layer map, phase sequence, shared type index and the `StockpileApiFactory` helper index. `NN-<slug>.md` is the plan for each phase (01–07). Read the overview and the current phase file before implementing phase work. Plans cite a spec by section (`§6`, `§8`, …); code comments use the same numbering.
- `docs/reviews/backend/` holds the phase reviews.
- **One branch per phase:** cut `phase-NN-<slug>` from `dev` before the first commit. `dev` is the integration branch and `main` holds released work. Phase branches fast-forward into `dev` (`--ff-only`), so history is linear. Use `git branch -a`, not `git log`, to see where a phase stands.
- The README "Status" line and its per-project test counts can lag behind the code. Trust the branches and the tests.

## Architecture

Clean Architecture in four projects: `Domain` ← `Application` ← `Infrastructure` / `Api`. `Stockpile.Architecture.Tests` enforces the dependency rule. `Domain` references nothing. `Application` may not reference EF's relational provider, Npgsql, SignalR, ASP.NET Core or `Infrastructure`. **Only `SystemClock` may read the wall clock**, which a Mono.Cecil IL scan checks. Get every timestamp from `IClock` and never use `DateTime(Offset).UtcNow/Now`.

### Request flow

Endpoint (`Api/Endpoints/<Feature>Endpoints.cs`, a `Map<Feature>Endpoints` extension) → `ISender.Send` → MediatR pipeline `LoggingBehavior` → `ValidationBehavior` → `TransactionBehavior` → handler → `Result`/`Result<T>` → `ResultExtensions.ToOk/ToCreated/ToHttpResult`, which maps to RFC 7807 problem details.

- Requests implement `ICommand` / `ICommand<T>` (these return `Result`) or `IQuery<T>`. `TransactionBehavior` wraps **only commands**. Validation runs outside the transaction on purpose.
- Folder layout is one request per folder: `Application/<Feature>/Commands/<Name>/<Name>Command.cs`, plus `Handler`/`Validator`. Some small commands keep the validator (and sometimes the handler) in the command file.
- `UnitOfWork.ExecuteInTransactionAsync` **rolls back when the handler returns a failed `Result`**. Multi-line operations such as confirm and transfer legs depend on this for all-or-nothing behaviour. Do not remove it.
- `TransactionBehavior` replays a command once on `IdempotencyReplayException`, which is a lost idempotency race. It retries up to 3 times on `TransientConflictException` (deadlock or serialization failure).

### Errors and HTTP status

Domain outcomes are `Result` values carrying an `Error` subtype, never exceptions. `ResultExtensions.Map` is the single mapping, and the exception handler also routes through it:
- `422` for domain rule violations, **including insufficient stock** (`InsufficientStockError`)
- `409` only for an optimistic-concurrency conflict on an edit-style aggregate, idempotency-key reuse with a different request, or a transient conflict that exhausted its retries
- `404` for a missing entity, or a missing (product, warehouse) stock row
- `500` if a stock CHECK constraint fires, because that is a defect

When you add an error type with extra payload, extend the `switch` in `ResultExtensions.Problem`.

### Stock mutations (the core invariant)

- `stock_items` is changed **only** through `IStockWriter`, and each method is one atomic conditional `UPDATE … RETURNING`. Implemented in `Infrastructure/Persistence/StockWriter.cs` / `StockSql.cs`, it runs on the DbContext's own connection and ambient transaction, and **throws if no transaction is open**. `StockItem` has no concurrency token. Handlers must never load and re-save `StockItem` through the change tracker, because tracked instances go stale after a raw write.
- Handlers go through `IStockMutator.ApplyAsync` (`Application/Common/Stock/StockMutation.cs`), which runs every mutation in the same order: idempotency replay check → conditional UPDATE → refusal mapping → append the `StockMovement` with after-values **taken from RETURNING** → enqueue a notification.
- `stock_movements` is append-only, and a DB trigger blocks UPDATE and DELETE. Each row carries two signed deltas (on-hand, reserved) and two after-snapshots.
- **Idempotency:** every stock-mutating endpoint requires a client key, and the key is persisted on the movement with a request hash. If a replayed key comes with a different hash, the request is refused (`IdempotencyKeyReusedError`, 409). Order and transfer commands write one movement per line and leg, so they derive keys through `DerivedIdempotencyKey.For(clientKey, leg, lineId)`. New legs must be added to `DerivedIdempotencyKey.Legs`, and client keys are capped at `MaxClientKeyLength` (50). Commands that write no movement use `IProcessedRequestStore`.
- **Lock ordering:** a command that writes several stock rows calls `IStockWriter.LockRowsAsync` for all of them up front, which locks in id order, before its first write.
- **Transfers** go From → an `InTransit`-kind warehouse → To. Both legs of each line run in one transaction. The unit cost is read at dispatch under the row lock, recorded on the transfer line, and reused at receipt, which conserves total valuation.
- Costing is weighted average in integer cents (`long`), single currency. There is no FIFO and no `decimal` money.
- Edit-style aggregates (`Product`, `PurchaseOrder`, `SalesOrder`, `StockTransfer`) use Postgres `xmin` as an optimistic concurrency token.

### Notifications vs domain events

`Entity.Raise` records domain events that the unit tests assert on, but **nothing dispatches them and nothing should**. EF configurations `Ignore(x => x.DomainEvents)`. Real-time delivery is separate: handlers call `INotificationPublisher.Enqueue`, and `TransactionBehavior` flushes only after commit. It clears the queue on failure, rollback or retry.

### Other conventions

- IDs are UUIDv7 (`Guid.CreateVersion7()`).
- DB naming is snake_case (EFCore.NamingConventions), so raw SQL uses snake_case columns. There is one `IEntityTypeConfiguration<T>` per entity in `Infrastructure/Persistence/Configurations/`.
- Enums are serialized as strings over the wire.
- Read paths are LINQ projections evaluated in SQL (`.Select(x => new Dto(...))`). Mapperly is referenced but deliberately unused. Aggregates are built through validating factory methods (`Product.Create(...)` returns `Result<Product>`).
- Authorization policies live in `Api/Authorization/Policies.cs` (`CanView`, `CanOperate`, `CanManageStock`, `CanViewCosts`, `CanAdminister`).
- The dev JWT signing key in `appsettings.Development.json` is committed on purpose. Production reads `Jwt__SigningKey` from the environment.

## Integration tests

- `StockpileApiFactory` (a `WebApplicationFactory<Program>`) starts one `postgres:16-alpine` Testcontainer per run and applies the real migrations. It runs in environment `Testing`, not `Development`, and supplies its own JWT settings. Tests join `[Collection(nameof(ApiCollection))]` and call `ResetDatabaseAsync()`, which truncates every table and resets the document-number sequences.
- Use the factory helpers (`CreateClientAs(Role)`, `SeedWarehouseAsync`, `SeedProductAsync`, `SeedStockAsync`, `WithWriterAsync`, `ReadStockAsync`, `TotalValuationAsync`, …) rather than one-off setup. `SeedStockAsync` also writes the opening-balance movement so reconciliation stays at zero. Calls to `IStockWriter` must go through `WithWriterAsync`, which provides the ambient transaction. Add new helpers to the fixture index in `00-overview.md`.
- `PolicyProbeEndpoints` and `FaultProbeEndpoints` are test-only routes, added through `TestRouteStartupFilter`.
- xUnit v3: `IAsyncLifetime` members return `ValueTask`. A `Task` signature compiles as an unrelated method, and the container silently never starts.
