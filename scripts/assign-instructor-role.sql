-- Assign Instructor role to nana@instruct.com
INSERT INTO identity.user_roles ("UserId", "RoleId")
SELECT u."Id", r."Id"
FROM identity.users u
CROSS JOIN identity.roles r
WHERE u."Email" = 'nana@instruct.com'
  AND r."Name" = 'Instructor'
  AND NOT EXISTS (
	  SELECT 1 FROM identity.user_roles
	  WHERE "UserId" = u."Id" AND "RoleId" = r."Id"
  );

SELECT u."Email", string_agg(r."Name", ', ') AS roles
FROM identity.users u
LEFT JOIN identity.user_roles ur ON u."Id" = ur."UserId"
LEFT JOIN identity.roles r ON ur."RoleId" = r."Id"
WHERE u."Email" = 'nana@instruct.com'
GROUP BY u."Email";
