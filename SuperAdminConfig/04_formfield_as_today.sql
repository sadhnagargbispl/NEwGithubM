/* =============================================================================
   04_formfield_as_today.sql - form field rules: empty = "as today"

   RUN ON: the MASTER database (same as 01), only if 01 was run BEFORE 17 Oct 2026.
           (01 now creates these columns this way already; running 04 again is harmless.)

   WHY: in Cfg_FormField, Show / Required / Read-only were yes-no columns that
   could never be empty. Saving only a field's label would then also save
   "Required = No" and silently make a field optional that the panel requires
   today. After this script, an empty value means "the panel decides, as today",
   and only what is actually chosen in Super Admin changes the panel.

   SAFE: changes only Cfg_FormField (a Super Admin table, created by 01).
   Existing rows keep their values. No panel table is touched.
   ============================================================================= */

SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.Cfg_FormField', 'U') IS NULL
BEGIN
    RAISERROR('Cfg_FormField does not exist - run 01_config_tables.sql first.', 16, 1);
    RETURN;
END
GO

/* Remove the "always 1 / always 0" defaults on the three columns. */
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql += N'ALTER TABLE dbo.Cfg_FormField DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';'
  FROM sys.default_constraints dc
  JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
 WHERE dc.parent_object_id = OBJECT_ID('dbo.Cfg_FormField')
   AND c.name IN ('IsVisible', 'IsRequired', 'IsReadOnly');
EXEC sp_executesql @sql;
GO

/* Allow empty (NULL) = as today. */
ALTER TABLE dbo.Cfg_FormField ALTER COLUMN IsVisible  BIT NULL;
ALTER TABLE dbo.Cfg_FormField ALTER COLUMN IsRequired BIT NULL;
ALTER TABLE dbo.Cfg_FormField ALTER COLUMN IsReadOnly BIT NULL;
GO

/* Check: is_nullable should be 1 for all three. */
SELECT name, is_nullable FROM sys.columns
 WHERE object_id = OBJECT_ID('dbo.Cfg_FormField') AND name IN ('IsVisible', 'IsRequired', 'IsReadOnly');
