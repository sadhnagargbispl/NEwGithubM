/* =====================================================================
   07_server_address_setting.sql  -  no SQL server IP written inside code
   ---------------------------------------------------------------------
   Run on the MASTER database. Safe to run again.

   M_ConnectionMaster rows may say "localhost,1533" (written when the
   panels ran on the SQL machine). Proc_GetConnection1 used to swap that
   for a FIXED IP typed into the procedure. Now the address comes from the
   Super Admin setting  db.server_address  (Cfg_Feature, CompanyID 0 =
   All companies). Empty / switched off = the connection string is given
   exactly as it is stored.
   ===================================================================== */
SET NOCOUNT ON;

-- the setting (only created if missing - an existing value is never overwritten)
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 0 AND FeatureKey = 'db.server_address')
    INSERT INTO dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt)
    VALUES (0, 'db.server_address', N'43.242.226.223,1533', 1, N'07_server_address_setting.sql', GETDATE());
GO

ALTER PROCEDURE dbo.Proc_GetConnection1   -- Proc_GetConnection1 1007
(
    @CompanyId int
)
AS
BEGIN
    -- SQL server address from Super Admin (setting db.server_address); NULL = leave as stored
    DECLARE @Server nvarchar(2000) =
        (SELECT TOP 1 NULLIF(LTRIM(RTRIM(FeatureValue)), N'')
           FROM dbo.Cfg_Feature
          WHERE CompanyID = 0 AND FeatureKey = 'db.server_address' AND IsEnabled = 1);

    SELECT CASE WHEN @Server IS NULL THEN ConnectionString
                ELSE REPLACE(ConnectionString, 'localhost,1533', @Server) END AS ConnectionString,
           DatabaseName
      FROM (SELECT '"' AS R, ConnectionString, DatabaseName
              FROM dbo.M_ConnectionMaster
             WHERE CompanyID = @CompanyId AND ConnTypeID = 1) AS a

    SELECT DatabaseName FROM dbo.M_ConnectionMaster WHERE CompanyID = @CompanyId AND ConnTypeID = 2
    UNION ALL
    SELECT 'AA' AS DatabaseName FROM dbo.M_ConnectionMaster WHERE CompanyID = @CompanyId
END
GO
