/* =============================================================================
   03_superadmin_users.sql - who can sign in to the new Super Admin (C#)

   RUN ON: the MASTER database (same as 01_config_tables.sql - the one the
           panels' "sconstr" connection points to).

   SAFE TO RUN:
     - Creates NEW tables only. Nothing existing is changed or deleted
       (the old, empty TblSuperAdminLogin is left alone - it stores passwords
       as plain text in 30 characters, too short for a secure hash).
     - Can be run again any time; whatever already exists is skipped.

   The first Super Admin owner is created from the Super Admin website itself
   (the "first-time setup" page, which needs the setup key from the site's
   settings) - so no password is ever written in a script.
   ============================================================================= */

SET NOCOUNT ON;
GO

/* Super Admin users. Passwords are stored only as a salted hash
   (ASP.NET Core Identity PasswordHasher, PBKDF2). */
IF OBJECT_ID('dbo.SA_User', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SA_User (
        UserID        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SA_User PRIMARY KEY,
        UserName      VARCHAR(50)    NOT NULL,
        FullName      NVARCHAR(100)  NOT NULL,
        Email         NVARCHAR(150)  NULL,
        Mobile        VARCHAR(15)    NULL,
        PasswordHash  NVARCHAR(300)  NOT NULL,
        Role          VARCHAR(20)    NOT NULL CONSTRAINT DF_SA_User_Role DEFAULT ('admin'),  -- owner | admin | viewer
        IsActive      BIT            NOT NULL CONSTRAINT DF_SA_User_IsActive DEFAULT (1),
        FailedCount   INT            NOT NULL CONSTRAINT DF_SA_User_FailedCount DEFAULT (0),
        LockedUntil   DATETIME       NULL,
        LastLoginAt   DATETIME       NULL,
        CreatedBy     NVARCHAR(100)  NULL,
        CreatedAt     DATETIME       NOT NULL CONSTRAINT DF_SA_User_CreatedAt DEFAULT (GETDATE()),
        CONSTRAINT UQ_SA_User_UserName UNIQUE (UserName)
    );
END
GO

/* Every sign-in attempt (success or not) - for security review. */
IF OBJECT_ID('dbo.SA_LoginHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SA_LoginHistory (
        LogID      BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SA_LoginHistory PRIMARY KEY,
        UserName   VARCHAR(50)    NOT NULL,
        Success    BIT            NOT NULL,
        Reason     VARCHAR(50)    NULL,       -- ok | bad_password | locked | inactive | unknown_user
        IpAddress  VARCHAR(50)    NULL,
        UserAgent  NVARCHAR(300)  NULL,
        LoggedAt   DATETIME       NOT NULL CONSTRAINT DF_SA_LoginHistory_LoggedAt DEFAULT (GETDATE())
    );
    CREATE INDEX IX_SA_LoginHistory_At ON dbo.SA_LoginHistory (LoggedAt DESC);
END
GO

/* Check: both tables should be listed. */
SELECT name FROM sys.tables WHERE name IN ('SA_User', 'SA_LoginHistory');
