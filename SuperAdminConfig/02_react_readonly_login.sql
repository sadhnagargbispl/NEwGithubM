/* =====================================================================
   02_react_readonly_login.sql
   A READ-ONLY SQL login for the new React panels' API, so that while the
   new screens are being built against the live company databases they
   can never change anything.

   What it gets:
     - db_datareader   : SELECT on tables and views
     - VIEW DEFINITION : read stored procedure code (so each one can be
                         checked before it is allowed)
   What it does NOT get:
     - no INSERT / UPDATE / DELETE, no EXECUTE on any procedure.
       (A procedure owned by dbo can write to tables even when the caller
       only has read rights, so procedures are allowed one by one later,
       in a separate script, only after they are checked to be SELECT-only.)

   HOW TO RUN (on server 43.242.226.223,1533, as a sysadmin):
     1. Replace  CHANGE_ME_Strong#Passw0rd  below with a strong password.
     2. Run the whole script once. It is safe to run again.
     3. Put the password in the API on the dev machine only (not in git):
          dotnet user-secrets set "LegacyMember:ReadOnlyPassword" "<password>"
   ===================================================================== */

USE [master];
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'usr_react_readonly')
    CREATE LOGIN [usr_react_readonly] WITH PASSWORD = N'CHANGE_ME_Strong#Passw0rd', CHECK_POLICY = ON;
GO

/* Company 1114 - Expert Holistic: MLM DB, its select (read) DB, inventory DB */
USE [expertholistic];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'usr_react_readonly')
    CREATE USER [usr_react_readonly] FOR LOGIN [usr_react_readonly];
ALTER ROLE [db_datareader] ADD MEMBER [usr_react_readonly];
GRANT VIEW DEFINITION TO [usr_react_readonly];
GO

USE [expertholisticsselect];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'usr_react_readonly')
    CREATE USER [usr_react_readonly] FOR LOGIN [usr_react_readonly];
ALTER ROLE [db_datareader] ADD MEMBER [usr_react_readonly];
GRANT VIEW DEFINITION TO [usr_react_readonly];
GO

USE [expertholisticinv];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'usr_react_readonly')
    CREATE USER [usr_react_readonly] FOR LOGIN [usr_react_readonly];
ALTER ROLE [db_datareader] ADD MEMBER [usr_react_readonly];
GRANT VIEW DEFINITION TO [usr_react_readonly];
GO

/* Check: should list db_datareader in each database, and nothing else. */
SELECT 'expertholistic' AS db, r.name AS role_name
  FROM [expertholistic].sys.database_role_members m
  JOIN [expertholistic].sys.database_principals r ON r.principal_id = m.role_principal_id
  JOIN [expertholistic].sys.database_principals u ON u.principal_id = m.member_principal_id
 WHERE u.name = N'usr_react_readonly';
