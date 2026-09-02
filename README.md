# Simulation Platform Backend

Modular-monolith foundation for a multiplayer educational simulation platform.

Implemented: versioned model contracts, dynamic capabilities, a configurable phase state machine,
idempotent action handling, append-only events, state projections, SignalR, health checks, tests,
and an isolated Supply/Demand demonstration model.

Persistence currently uses an in-memory adapter. `IRuntimeStore` and `IScenarioCatalog` are the
replacement boundaries for PostgreSQL and Entity Framework Core.

```powershell
dotnet build SimulationPlatform.slnx
dotnet test SimulationPlatform.slnx
dotnet run --project src/SimulationPlatform.Api
```
