-- sql-bootstrap.sql
-- One-time setup: creates Entra-based SQL users for each managed identity.
-- Run this script ONCE per environment as the SQL Entra administrator.
--
-- Prerequisites:
--   1. Connect to the SQL server as the Entra admin configured in sql.bicep
--      (the group/user whose object ID was passed as sqlEntraAdminObjectId).
--   2. SQL public network access is disabled — connect via VNet (jump box / Bastion)
--      or temporarily enable public access in main.bicep for bootstrapping.
--   3. Replace <prefix> with your environment prefix (e.g. stocky-prod).
--
-- Run with: sqlcmd -S <server>.database.windows.net -d <database> --authentication-method ActiveDirectoryInteractive -i sql-bootstrap.sql
--
-- After this script completes:
--   - The API container app can authenticate to SQL via the apiSqlId managed identity.
--   - The migrator ACA job can run EF migrations via the migratorId managed identity.
--   - The CI/CD pipeline can run migrations via the cicdId managed identity.
-- -----------------------------------------------------------------------

USE [stocky]; -- replace with your database name if different

-- -----------------------------------------------------------------------
-- API runtime identity: read + write access (least privilege).
-- Identity name: id-<prefix>-api-sql
-- -----------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'id-<prefix>-api-sql')
BEGIN
    CREATE USER [id-<prefix>-api-sql] FROM EXTERNAL PROVIDER;
    PRINT 'Created user: id-<prefix>-api-sql';
END
ELSE
BEGIN
    PRINT 'User already exists: id-<prefix>-api-sql';
END;

ALTER ROLE db_datareader ADD MEMBER [id-<prefix>-api-sql];
ALTER ROLE db_datawriter ADD MEMBER [id-<prefix>-api-sql];
PRINT 'Granted db_datareader + db_datawriter to id-<prefix>-api-sql';

-- -----------------------------------------------------------------------
-- Migrator job identity: db_owner so EF Core migrations can create/alter tables.
-- Identity name: id-<prefix>-migrator
-- -----------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'id-<prefix>-migrator')
BEGIN
    CREATE USER [id-<prefix>-migrator] FROM EXTERNAL PROVIDER;
    PRINT 'Created user: id-<prefix>-migrator';
END
ELSE
BEGIN
    PRINT 'User already exists: id-<prefix>-migrator';
END;

ALTER ROLE db_owner ADD MEMBER [id-<prefix>-migrator];
PRINT 'Granted db_owner to id-<prefix>-migrator';

-- -----------------------------------------------------------------------
-- CI/CD identity: db_owner for running migrations from the pipeline.
-- Identity name: id-<prefix>-cicd
-- -----------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'id-<prefix>-cicd')
BEGIN
    CREATE USER [id-<prefix>-cicd] FROM EXTERNAL PROVIDER;
    PRINT 'Created user: id-<prefix>-cicd';
END
ELSE
BEGIN
    PRINT 'User already exists: id-<prefix>-cicd';
END;

ALTER ROLE db_owner ADD MEMBER [id-<prefix>-cicd];
PRINT 'Granted db_owner to id-<prefix>-cicd';

-- -----------------------------------------------------------------------
-- Verify
-- -----------------------------------------------------------------------
SELECT dp.name AS [user], dp.type_desc, rp.name AS [role]
FROM sys.database_role_members drm
JOIN sys.database_principals dp ON drm.member_principal_id = dp.principal_id
JOIN sys.database_principals rp ON drm.role_principal_id  = rp.principal_id
WHERE dp.name IN ('id-<prefix>-api-sql', 'id-<prefix>-migrator', 'id-<prefix>-cicd')
ORDER BY dp.name, rp.name;