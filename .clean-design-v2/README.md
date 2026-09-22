# Simulation Platform Backend

Modular-monolith foundation for a multiplayer educational simulation platform.

Implemented: versioned model contracts, dynamic capabilities, a configurable phase state machine,
idempotent action handling, append-only events, state projections, SignalR, health checks, tests,
and an isolated Supply/Demand demonstration model.

API runtime persistence uses PostgreSQL/EF Core behind `IRuntimeStore` and `IScenarioCatalog`.
Milestone 1B adds ASP.NET Core Identity, JWT access
tokens, rotating hashed refresh tokens, resource policies, serializable command transactions,
declarative rules, migrations, auditing tables, and an outbox dispatcher. The in-memory adapter remains
available only as a unit-test adapter.

Production secrets are not committed. Configure `ConnectionStrings__Platform`,
`Authentication__Jwt__Issuer`, `Authentication__Jwt__Audience`, and a random minimum-256-bit
`Authentication__Jwt__SigningKey` through the deployment secret store. `compose.yaml` contains local-only
development credentials.

The Milestone 1B design and invariants are documented in `docs/milestone-1b-design.md`.

```powershell
dotnet build SimulationPlatform.slnx
dotnet test SimulationPlatform.slnx
dotnet run --project src/SimulationPlatform.Api
```
