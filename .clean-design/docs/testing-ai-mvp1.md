# AI-MVP-1 verification

The unit and integration projects use the repository's normal `dotnet test` commands. The integration suite requires a disposable PostgreSQL instance; when Testcontainers is added, it must fail clearly if Docker is unavailable rather than silently skipping.

Before building, stop the API/debugger and any `dotnet watch` process that owns files under `src/SimulationPlatform.Api/bin`. Verify no process is holding those assemblies, then run:

```text
dotnet build src\SimulationPlatform.Api --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet test tests\Tests.Unit\Tests.Unit.csproj --no-restore
dotnet test tests\Tests.Integration\Tests.Integration.csproj --no-restore
```

When investigating a test-runner hang, use `--no-build --list-tests` first. In this environment discovery completes successfully, while build-enabled commands can stall during MSBuild/artifact handling. The currently available integration project contains the existing HTTP/PostgreSQL-oriented tests; it does not yet provision a disposable PostgreSQL container, so AI-MVP-1 database acceptance tests remain environment-dependent until Testcontainers is introduced.

The idempotency acceptance suite must cover exact replay, same-key conflicts, actor isolation, concurrent approval/rejection races, rollback checkpoints, and Macro/CompetitiveMarket publish paths. Test counts must be reported from the actual run.

Disposable PostgreSQL verification uses `Testcontainers.PostgreSql` 4.7.0 with the pinned `postgres:16-alpine` image. Docker must be running; the fixture fails clearly when the Docker endpoint is unavailable. It starts PostgreSQL, applies the complete EF migration chain with `Database.MigrateAsync()`, and disposes the container after the test collection. The existing `ClassroomDatabase` fixture remains available for the legacy suite during migration.

Run the disposable migration smoke test with:

```text
dotnet test tests\\Tests.Integration\\Tests.Integration.csproj --no-build --no-restore -p:BaseOutputPath=.artifacts\\ai-mvp1-verification\\bin\\ --filter "FullyQualifiedName~DisposablePostgresSmokeTests"
```

SDK 10.0.302 restore/build workaround remains required where applicable: `RestoreUseStaticGraphEvaluation=true`, `NuGetAudit=false`, and `BuildInParallel=false`.

When normal API outputs are locked, build the integration project with an isolated command-line output root (without changing project files):

```text
dotnet build tests\\Tests.Integration\\Tests.Integration.csproj --no-restore -p:BaseOutputPath=.artifacts\\ai-mvp1-verification\\bin\\ -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet test tests\\Tests.Integration\\Tests.Integration.csproj --no-build --no-restore -p:BaseOutputPath=.artifacts\\ai-mvp1-verification\\bin\\ --filter "FullyQualifiedName~AiMvp1ProposalSmokeTests"
```
