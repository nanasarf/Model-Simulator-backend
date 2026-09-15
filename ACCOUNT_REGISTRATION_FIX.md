# Quick Fix: Account Registration Issue

## The Problem
Your account registration is failing because the database migrations haven't been applied to PostgreSQL. The error logs show:
- `ERROR: relation "__EFMigrationsHistory" does not exist`
- `ERROR: column "user_id" of relation "user_roles" does not exist`

## The Fix

### Quick Steps:
1. **Stop and reset the database** (this deletes all data - OK for local development):
   ```powershell
   docker compose down -v
   docker compose up -d postgres
   # Wait ~10 seconds for PostgreSQL to start
   ```

2. **Run your application** (locally or via Docker):
   ```powershell
   dotnet run --project src/SimulationPlatform.Api
   ```

3. **Watch the startup logs** for this message:
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

4. **Test registration** - Try creating an account again at `POST /api/v1/auth/register`

## What I Fixed

✅ **Improved DatabaseInitializer logging** - Now you can see exactly what's happening during database initialization instead of silent failures.

The `DatabaseInitializer.cs` now logs:
- When initialization starts
- When each context's migrations are being applied
- When each role is created
- Clear error messages if something fails

This makes it much easier to diagnose database issues in the future.

## Why This Happens

Entity Framework Core needs to:
1. Create the migrations history table (`__EFMigrationsHistory`)
2. Apply all pending migrations to create your schema
3. Create required roles for the application

If PostgreSQL isn't ready when your app starts, or the connection fails, these steps don't happen. Now you'll see detailed logs instead of silent failures.

## Detailed Troubleshooting

See `docs/DATABASE_FIX.md` for:
- More troubleshooting steps
- Manual database setup commands
- Docker connection string configuration
- Verification queries

## Connection Details

Make sure your connection string in `appsettings.Development.json` matches:
```json
{
  "ConnectionStrings": {
	"Platform": "Host=localhost;Port=5432;Database=simulation_platform;Username=simulation_platform;Password=local-development-only"
  }
}
```

If running .NET app inside Docker (same network as PostgreSQL), use:
```
Host=postgres;Port=5432;Database=simulation_platform;Username=simulation_platform;Password=local-development-only
```
