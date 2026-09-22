-- Create Instructor Account: nana@instruct.com
-- Password: String12!
-- This script sets up an instructor account manually via SQL

-- Step 1: Create the user (if not already registered via API)
INSERT INTO identity.users (
	id,
	"UserName",
	"NormalizedUserName",
	email,
	"NormalizedEmail",
	"EmailConfirmed",
	"PasswordHash",
	"SecurityStamp",
	"ConcurrencyStamp",
	"PhoneNumber",
	"PhoneNumberConfirmed",
	"TwoFactorEnabled",
	"LockoutEnd",
	"LockoutEnabled",
	"AccessFailedCount",
	"CreatedAt",
	"IsActive"
)
SELECT
	gen_random_uuid(),
	'nana@instruct.com',
	'NANA@INSTRUCT.COM',
	'nana@instruct.com',
	'NANA@INSTRUCT.COM',
	true,
	-- This is a bcrypt hash of "String12!" 
	-- For production, ensure password is properly hashed
	'$2a$11$DXv3DGEo7eow50ZexwuCOuVjUc9y4d5Ri9QWuIYFNIUgLvjure5Pe',
	gen_random_uuid()::text,
	gen_random_uuid()::text,
	null,
	false,
	false,
	null,
	true,
	0,
	now(),
	true
)
WHERE NOT EXISTS (
	SELECT 1 FROM identity.users 
	WHERE "NormalizedEmail" = 'NANA@INSTRUCT.COM'
);

-- Step 2: Ensure Instructor role exists
INSERT INTO identity.roles (id, name, "NormalizedName", "ConcurrencyStamp")
SELECT gen_random_uuid(), 'Instructor', 'INSTRUCTOR', gen_random_uuid()::text
WHERE NOT EXISTS (SELECT 1 FROM identity.roles WHERE name = 'Instructor');

-- Step 3: Assign Instructor role to the user
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

-- Step 4: Verify the user and role assignment
SELECT 
	u.id,
	u.email,
	u."NormalizedEmail",
	u."IsActive",
	string_agg(r.name, ', ') as roles,
	u."CreatedAt"
FROM identity.users u
LEFT JOIN identity.user_roles ur ON u.id = ur."UserId"
LEFT JOIN identity.roles r ON ur."RoleId" = r.id
WHERE u.email = 'nana@instruct.com'
GROUP BY u.id, u.email, u."NormalizedEmail", u."IsActive", u."CreatedAt";

-- Expected output: A row showing email nana@instruct.com with role "Student, Instructor" or just "Instructor"
