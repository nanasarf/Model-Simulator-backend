# Docker Commands Quick Reference

## Reset Database Completely (Local Development)
```powershell
# Stop containers and remove volumes
docker compose down -v

# Start PostgreSQL fresh
docker compose up -d postgres

# Verify it's running
docker compose logs postgres -f
# Look for: "database system is ready to accept connections"
```

## View Database Logs
```powershell
# All PostgreSQL logs
docker compose logs postgres

# Follow logs in real-time
docker compose logs postgres -f

# Last 50 lines
docker compose logs postgres --tail=50
```

## Access PostgreSQL Directly
```powershell
# Connect to database
docker compose exec postgres psql -U simulation_platform -d simulation_platform

# List tables
\dt identity.*
\dt runtime.*

# View table schema
\d identity.users
\d identity.user_roles

# Exit
\q
```

## Test Database Connection
```powershell
# Connect as postgres (admin) user
docker compose exec postgres psql -U postgres -d simulation_platform -c "SELECT version();"

# Check if migrations table exists
docker compose exec postgres psql -U simulation_platform -d simulation_platform -c "SELECT * FROM \"__EFMigrationsHistory\" LIMIT 5;"
```

## Restart Everything
```powershell
# Graceful restart (preserves data)
docker compose restart

# Hard restart (clears everything)
docker compose down -v
docker compose up -d
```

## Check Container Status
```powershell
# See all containers
docker compose ps

# Inspect postgres container
docker compose inspect postgres
```

## Manual Database Setup (if needed)
```powershell
# Create database and user manually
docker compose exec postgres psql -U postgres -c "
CREATE USER IF NOT EXISTS simulation_platform WITH PASSWORD 'local-development-only';
ALTER ROLE simulation_platform WITH CREATEDB;
CREATE DATABASE IF NOT EXISTS simulation_platform OWNER simulation_platform;
GRANT ALL PRIVILEGES ON DATABASE simulation_platform TO simulation_platform;
"
```

## View Real-Time Activity
```powershell
# Terminal 1: Watch database logs
docker compose logs postgres -f

# Terminal 2: Watch app startup
dotnet run --project src/SimulationPlatform.Api

# You'll see:
# 1. PostgreSQL initialization messages
# 2. App connecting to database
# 3. Migrations being applied
# 4. Roles being created
# 5. "Application started" message
```

## Troubleshoot Connection Issues
```powershell
# Verify postgres container is running
docker compose ps | grep postgres

# Check if port 5432 is listening
netstat -ano | findstr ":5432"

# Or use Docker's native networking check
docker compose exec postgres netstat -tlnp | grep 5432
```

## Clean Up Docker Resources
```powershell
# Remove unused volumes
docker volume prune

# Remove unused networks
docker network prune

# View all volumes
docker volume ls

# Remove specific volume
docker volume rm modelsimulator-backend_postgres_data
```
