# Creating an Instructor Account

This guide walks you through creating an instructor account with the credentials:
- **Email**: nana@instruct.com
- **Password**: String12!

## Option 1: Automated PowerShell Script (Recommended)

### Prerequisites
- Application running on `http://localhost:5000` (or specify custom URL)
- Docker with `docker compose` available

### Run the Script

```powershell
# Using default values
.\scripts\create-instructor.ps1

# Or specify custom values
.\scripts\create-instructor.ps1 -Email "custom@email.com" -Password "YourPass123!" -ApiUrl "http://localhost:5000"
```

### What It Does
1. Registers the account via the API (automatically gets Student role)
2. Connects to the database via Docker
3. Assigns the Instructor role
4. Verifies the assignment

### Expected Output
```
=== Simulation Platform: Create Instructor Account ===

Email:   nana@instruct.com
API URL: http://localhost:5000

Step 1: Registering account via API...
✓ Account registered successfully
  User ID: [UUID]

Step 2: Assigning Instructor role...
✓ Instructor role assigned successfully

Step 3: Verifying assignment...
✓ Instructor role confirmed
  Roles: Student
		 Instructor

=== Account Creation Complete ===

Instructor Account Details:
  Email:    nana@instruct.com
  Password: String12!
  Role:     Instructor
```

---

## Option 2: Manual API + SQL Approach

### Step 1: Register via API

Use cURL, Postman, or any HTTP client:

```powershell
$email = "nana@instruct.com"
$password = "String12!"

$body = @{
	email = $email
	password = $password
} | ConvertTo-Json

Invoke-WebRequest -Uri "http://localhost:5000/api/v1/auth/register" `
	-Method Post `
	-ContentType "application/json" `
	-Body $body
```

**Response** (save the `user.id`):
```json
{
  "user": {
	"id": "550e8400-e29b-41d4-a716-446655440000",
	"email": "nana@instruct.com"
  }
}
```

### Step 2: Assign Instructor Role via SQL

Connect to the database and execute:

```bash
docker compose exec postgres psql -U simulation_platform -d simulation_platform
```

Then paste this SQL:

```sql
-- Assign Instructor role to the user
INSERT INTO identity.user_roles ("UserId", "RoleId")
SELECT 
	u.id,
	r.id
FROM identity.users u
CROSS JOIN identity.roles r
WHERE u.email = 'nana@instruct.com'
  AND r.name = 'Instructor'
  AND NOT EXISTS (
	  SELECT 1 FROM identity.user_roles
	  WHERE "UserId" = u.id AND "RoleId" = r.id
  );

-- Verify
SELECT u.email, string_agg(r.name, ', ') as roles
FROM identity.users u
LEFT JOIN identity.user_roles ur ON u.id = ur."UserId"
LEFT JOIN identity.roles r ON ur."RoleId" = r.id
WHERE u.email = 'nana@instruct.com'
GROUP BY u.id, u.email;
```

**Expected Output**:
```
email            | roles
-----------------+------------------
nana@instruct.com| Student, Instructor
```

---

## Option 3: Direct SQL (Without API)

Use the provided SQL script:

```bash
docker compose exec postgres psql -U simulation_platform -d simulation_platform -f scripts/create-instructor.sql
```

Or manually in psql:

```bash
docker compose exec postgres psql -U simulation_platform -d simulation_platform
```

Then paste the contents of `scripts/create-instructor.sql`.

---

## Option 4: cURL (For Automation/CI/CD)

### Register the Account

```bash
curl -X POST http://localhost:5000/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{
	"email": "nana@instruct.com",
	"password": "String12!"
  }'
```

### Assign Instructor Role (via Docker/SQL)

```bash
docker compose exec postgres psql -U simulation_platform -d simulation_platform -c "
INSERT INTO identity.user_roles (\"UserId\", \"RoleId\")
SELECT u.id, r.id
FROM identity.users u
CROSS JOIN identity.roles r
WHERE u.email = 'nana@instruct.com'
  AND r.name = 'Instructor'
  AND NOT EXISTS (
	  SELECT 1 FROM identity.user_roles
	  WHERE \"UserId\" = u.id AND \"RoleId\" = r.id
  );
"
```

---

## Verify the Account

### Via API Login

```powershell
$loginBody = @{
	email = "nana@instruct.com"
	password = "String12!"
} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "http://localhost:5000/api/v1/auth/login" `
	-Method Post `
	-ContentType "application/json" `
	-Body $loginBody

$response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 5
```

Expected response includes `roles: ["Instructor"]`

### Via SQL Query

```bash
docker compose exec postgres psql -U simulation_platform -d simulation_platform -c "
SELECT 
	u.email,
	u.id,
	u.\"IsActive\",
	string_agg(r.name, ', ') as roles
FROM identity.users u
LEFT JOIN identity.user_roles ur ON u.id = ur.\"UserId\"
LEFT JOIN identity.roles r ON ur.\"RoleId\" = r.id
WHERE u.email = 'nana@instruct.com'
GROUP BY u.id, u.email, u.\"IsActive\";
"
```

Expected output:
```
email            | id                                   | IsActive | roles
-----------------+--------------------------------------+----------+---------------------
nana@instruct.com| 550e8400-e29b-41d4-a716-446655440000| t        | Student, Instructor
```

---

## Troubleshooting

### "Email already registered"
The account already exists. Either:
- Delete the user and try again
- Just assign the Instructor role via SQL (Option 3)

### "Role assignment failed"
Ensure the Instructor role exists:
```sql
SELECT * FROM identity.roles WHERE name = 'Instructor';

-- If empty, create it:
INSERT INTO identity.roles (id, name, "NormalizedName", "ConcurrencyStamp")
VALUES (gen_random_uuid(), 'Instructor', 'INSTRUCTOR', gen_random_uuid()::text);
```

### "Connection refused"
Check that:
1. PostgreSQL is running: `docker compose ps`
2. The database is ready: `docker compose logs postgres | grep "ready to accept"`
3. Connection string is correct in your app

### Script Permission Denied (PowerShell)
Allow script execution:
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

---

## Account Details

Once created, you can:

1. **Log in via API**:
   ```
   POST /api/v1/auth/login
   {
	 "email": "nana@instruct.com",
	 "password": "String12!"
   }
   ```

2. **Use in Frontend**: Use the access token from login response

3. **Access Instructor Features**:
   - Create classrooms
   - Create scenarios
   - Manage students
   - View analytics

---

## Files Reference

- **Automated Script**: `scripts/create-instructor.ps1`
- **SQL Script**: `scripts/create-instructor.sql`
- **This Guide**: `INSTRUCTOR_ACCOUNT_SETUP.md`

Choose the option that works best for your workflow!
