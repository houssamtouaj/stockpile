# Stockpile

A multi-warehouse inventory and order system whose stock quantities are derived from an
append-only movement ledger, whose stock mutations are correct under concurrency, and which
broadcasts changes live to every connected client across load-balanced API instances.

> **Status: phase 01 of 07 — foundations.** What is here is the skeleton every later phase
> builds on: the domain model and its invariants, the PostgreSQL schema with the check
> constraints and the append-only ledger trigger, the MediatR pipeline, authentication, and the
> architecture tests that keep the layering honest. The stock-mutation path itself (the atomic
> conditional `UPDATE … RETURNING` that makes concurrent reservations correct), orders,
> transfers, real-time broadcast and reporting land in phases 02–05. The only HTTP endpoints
> today are `/api/auth/*` and `/health/live`.

## Stack

.NET 10 LTS (C# 14) · ASP.NET Core Minimal APIs · EF Core 10 + Npgsql · PostgreSQL 16 ·
MediatR · FluentValidation · ASP.NET Core Identity + JWT bearer ·
xUnit v3 · Shouldly · NSubstitute · NetArchTest · Testcontainers

## Layout

```
src/Stockpile.Domain           entities, value objects, Result — no framework references
src/Stockpile.Application      MediatR handlers, pipeline behaviors, interfaces
src/Stockpile.Infrastructure   EF Core, Identity, JWT, the clock, the notification queue
src/Stockpile.Api              minimal API endpoints, authorization policies, composition root
tests/                         architecture, domain, application and API integration tests
```

Dependencies point inward — `Api`/`Infrastructure` → `Application` → `Domain` — and that is
enforced by a test, not by convention. See [Architecture rules](#architecture-rules-are-enforced-in-ci).

## Running it

```bash
# 1. Dependencies. Postgres is published on 5433, NOT 5432 — a native PostgreSQL install
#    commonly already owns 5432, and Docker will publish over it without complaining while
#    every host-side connection silently reaches the native server instead.
docker compose -f docker-compose.dev.yml up -d

# 2. Schema. Needs the EF tools: dotnet tool install --global dotnet-ef
dotnet ef database update -p src/Stockpile.Infrastructure -s src/Stockpile.Api

# 3. Run.
dotnet run --project src/Stockpile.Api
```

There are no seeded users yet — phase 07 brings the demo seeder. Until then, create one through
the same `UserManager` path the tests use, or drive the API through the integration tests.

## Tests

```bash
dotnet test tests/Stockpile.Architecture.Tests      #  4  layering and the clock rule
dotnet test tests/Stockpile.Domain.UnitTests        # 69  domain behaviour
dotnet test tests/Stockpile.Application.UnitTests   #  8  pipeline behaviors
dotnet test tests/Stockpile.Api.IntegrationTests    # 18  real PostgreSQL via Testcontainers
```

The integration tests need a running Docker daemon; they start their own PostgreSQL 16
container and apply the real migrations to it. Nothing is mocked at the database boundary.

## Things worth knowing before reading the code

### Architecture rules are enforced in CI

`tests/Stockpile.Architecture.Tests` asserts the dependency rule (`Domain` references nothing;
`Application` may not see EF's relational provider, Npgsql, SignalR, ASP.NET Core or
`Infrastructure`) and one rule that matters more than it looks: **only `SystemClock` may read
the wall clock.** Every timestamp in the system comes from `IClock`, which is what makes
backdated seed data and the slow-movers report testable at all.

That last rule works at the IL level through Mono.Cecil, because NetArchTest compares type
references and cannot see a call to a static property like `DateTimeOffset.UtcNow`. A rule that
passes on its first run is indistinguishable from a signature typo matching nothing, so it was
proven to fire by temporarily adding an offending type:

```
Inject IClock instead of reading the wall clock directly:
Stockpile.Application.ClockScratch.Sneaky -> get_UtcNow
Failed! - Failed: 1, Passed: 0, Skipped: 0, Total: 1
```

The offender list names the method, so the failure says where to go rather than only that
something broke. The same treatment is applied to the permission-matrix tests: see the note in
`RolePolicyTests` for what turns red when the role claim stops making the trip.

`.github/workflows/ci.yml` runs the architecture tests first — they are fast, and a layering
violation makes every downstream failure harder to read.

### Domain events are raised but never dispatched — on purpose

`Entity.Raise` exists so each aggregate can state, executably, what just happened, and the
domain unit tests assert on it (`Issue_emptyingTheShelf_raisesStockDepleted`,
`Receive_raisesStockChanged`). **There is no event dispatcher in this system and no phase
adds one.** Real-time delivery runs on a separate, explicit path: handlers call
`INotificationPublisher.Enqueue`, and `TransactionBehavior` flushes the queue only after the
transaction commits.

Two mechanisms for one job would be a design smell; what makes this one defensible is that only
one of them reaches a transport, and every EF configuration calls `builder.Ignore(x =>
x.DomainEvents)` so nothing is silently persisted. It is written down here because a reviewer
who sees a `DomainEvents` list will assume there is a dispatcher and go looking for it.

### Identifiers are UUIDv7, not UUIDv4

`Guid.CreateVersion7()` gives time-ordered UUIDs. On a table like `stock_movements` — which only
ever grows and is always queried by time range — random v4 keys scatter B-tree inserts across
the whole index, while v7 keys keep appending at the right-hand edge. Same 16 bytes, better
insert locality and better range scans.

### The development JWT signing key is committed on purpose

`src/Stockpile.Api/appsettings.Development.json` contains a real signing key. That is
deliberate: `docker compose up` and `dotnet run` have to work from a clean clone without a
setup ritual, and a key that only ever signs tokens for a local throwaway database protects
nothing worth protecting.

**Production reads it from the environment** — `Jwt__SigningKey`, like every other
configuration value, through the standard environment-variable provider. `appsettings.json`
(the non-Development file) carries no key at all, so there is nothing for a production
deployment to silently fall back to if it forgets to set one. The integration tests supply
their own key through `StockpileApiFactory`.

### Transfers never leave units in limbo

A transfer moves stock in two steps through a warehouse with `Kind = InTransit`: dispatch moves
it from the source into that warehouse, receipt moves it on to the destination. Each step
writes a paired `TransferOut` / `TransferIn` for every line, and **both legs of every line run
in one database transaction**, so a step either happens completely or not at all. That is the
mechanical reason total valuation is conserved mid-flight: at every moment each unit sits in
exactly one stock row, at one cost. `TransferTests.DispatchThenReceive_conservesTotalValuation`
checks it before dispatch, in flight and after receipt.

The cost a unit carries is the source's weighted average, read just before the outbound leg.
Two known imprecisions come with integer-cent weighted-average costing: a receipt that lands at
the source concurrently can make that figure marginally stale, and blending into a destination
that already holds the product at a different cost rounds the new average to the nearest cent.

## Not in scope

FIFO costing (weighted average only), multi-currency (integer cents, single currency) and
reversing an in-transit transfer are deliberate exclusions, not omissions. The reasoning for
each will be recorded here as the phase that meets it lands.

- **Cancelling a dispatched transfer** is refused (`transfer.cannot_cancel_after_dispatch`)
  rather than implemented. Undoing a dispatch means inventing compensating movements whose
  cost basis is ambiguous once the in-transit average has blended with other transfers. The
  honest operational answer is the one the error gives: receive it at the destination, then
  transfer it back.
- **Returns on shipped sales orders** are not modelled; cancelling a shipped order is refused
  with `so.already_shipped`.
