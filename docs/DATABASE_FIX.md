# Database Initialization Fix Guide

## Problem
Account registration fails with the following errors:
- `ERROR: relation "__EFMigrationsHistory" does not exist`
- `ERROR: column "user_id" of relation "user_roles" does not exist`

This indicates that Entity Framework migrations have not been applied to the PostgreSQL database.

## Root Cause
The database migrations in both `PlatformDbContext` and `IdentityDataContext` were not applied during the initial setup. The `DatabaseInitializer` hosted service attempts to apply these migrations, but if there are connection issues or permission problems, the errors are silently logged (in development mode) without preventing the application from starting.

## Solution

### Step 1: Stop and Clean the Database Container
```bash
docker compose down -v
# -v removes the named volumes, clearing the database
```

### Step 2: Restart PostgreSQL
```bash
docker compose up -d postgres
```

Wait for PostgreSQL to be fully ready (watch the logs for "database system is ready to accept connections"):
```bash
docker compose logs postgres -f
```

### Step 3: Rebuild and Run the Application
Make sure your application is configured to use the connection string pointing to PostgreSQL, then run:

```bash
dotnet build
dotnet run --project src/SimulationPlatform.Api
```

The `DatabaseInitializer` hosted service will:
1. Apply migrations from `PlatformDbContext` 
2. Apply migrations from `IdentityDataContext`
3. Create required roles (Student, Instructor, Administrator)
4. Log each step with detailed information

### Step 4: Verify Database Initialization

Check for these log messages indicating successful initialization:
```
Starting database initialization...
Applying PlatformDbContext migrations...
PlatformDbContext migrations applied successfully.
Applying IdentityDataContext migrations...
IdentityDataContext migrations applied successfully.
Creating platform roles...
Created role: Student
Created role: Instructor
Created role: Administrator
Database initialization completed successfully.
```

### Step 5: Test Account Registration

Try registering a new account via the API at `POST /api/v1/auth/register`

## Troubleshooting

### If Migrations Still Fail
1. Check database logs for connection issues:
   ```bash
   docker compose logs postgres | grep "ERROR\|FATAL"
   ```

2. Verify the connection string in `appsettings.Development.json`:
   - Host should be `localhost` (or `postgres` if running from Docker)
   - Port should be `5432`
   - Database should be `simulation_platform`
   - Username/password should match the PostgreSQL container setup

3. If permission issues exist, you may need to manually create the user:
   ```bash
   docker compose exec postgres psql -U postgres -c "CREATE USER simulation_platform WITH PASSWORD 'local-development-only';"
   docker compose exec postgres psql -U postgres -c "ALTER ROLE simulation_platform WITH CREATEDB;"
   docker compose exec postgres psql -U postgres -c "CREATE DATABASE simulation_platform OWNER simulation_platform;"
   ```

### If Migrations Apply But Registration Still Fails
1. Manually verify the schema was created:
   ```bash
   docker compose exec postgres psql -U simulation_platform -d simulation_platform -c "\dt identity.*"
   ```

   You should see tables like: `users`, `roles`, `user_roles`, `user_claims`, etc.

2. Check if the `design_time` user issue appears in logs - it's a red herring from GSSAPI authentication attempts and doesn't affect the application.

## Migration Files

### PlatformDbContext Migrations
Located in `src/SimulationPlatform.Infrastructure/Persistence/Migrations/`:
- `20260902060133_InitialPlatform.cs` - Initial schema
- `20260902060429_AddOutboxClaims.cs`
- `20260902061025_AddDeclarativeRules.cs`
- `20260903005617_Milestone2ClassroomWorkflow.cs`
- `20260903014634_Milestone4Gameplay.cs`
- `20260903035858_Milestone5ScenarioAuthoring.cs`
- `20260905054219_Milestone6LearningAnalytics.cs`

### IdentityDataContext Migrations
Located in `src/SimulationPlatform.Identity/Migrations/`:
- `20260902060142_InitialIdentity.cs` - Initial schema including `user_roles` table with both `UserId` and `RoleId` columns

## Connection String Configuration

For development (with Docker on the same machine):
```json
{
  "ConnectionStrings": {
	"Platform": "Host=localhost;Port=5432;Database=simulation_platform;Username=simulation_platform;Password=local-development-only"
  }
}
```

For Docker-to-Docker communication (if running the .NET app in Docker):
```json
{
  "ConnectionStrings": {
	"Platform": "Host=postgres;Port=5432;Database=simulation_platform;Username=simulation_platform;Password=local-development-only"
  }
}
```

## References
- Entity Framework Core Migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations
- PostgreSQL Docker Setup: https://hub.docker.com/_/postgres
