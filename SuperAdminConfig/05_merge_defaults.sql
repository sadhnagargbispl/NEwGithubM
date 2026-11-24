/* =============================================================================
   05_merge_defaults.sql - "All companies" defaults are combined value by value

   RUN ON: the MASTER database (same as 01), only if 01 was run BEFORE 17 Oct 2026.
           (01 now has this version of the procedure; running 05 again is harmless.)

   WHY: Cfg_GetCompanyConfig gave a panel EITHER the company's own branding /
   form-field row OR the "All companies" (0) row - never a mix. So a company
   that set only its own logo lost the colours set for all companies, and a
   company that only renamed a field lost a "hide" set for all companies.
   Now every value is taken from the company if set there, else from All
   companies, else it stays empty (= the panel works as today).

   Also: result 6 (version) now adds the All-companies version, the same way
   Cfg_GetVersion already does, so a change to a default is noticed too.

   SAFE: replaces one Super Admin procedure (created by 01). No table, no
   panel object and no data is changed.
   ============================================================================= */

SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.Cfg_GetCompanyConfig', 'P') IS NULL
    EXEC ('CREATE PROCEDURE dbo.Cfg_GetCompanyConfig AS SELECT 1;');
GO
ALTER PROCEDURE dbo.Cfg_GetCompanyConfig
    @CompanyID INT
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. labels
    SELECT l.LabelKey, l.LabelText
      FROM dbo.Cfg_Label l
     WHERE l.CompanyID = @CompanyID
        OR (l.CompanyID = 0 AND NOT EXISTS (SELECT 1 FROM dbo.Cfg_Label c
                                             WHERE c.CompanyID = @CompanyID AND c.LabelKey = l.LabelKey));

    -- 2. value types
    SELECT v.Code, v.DisplayName, v.IsEnabled, v.SortOrder
      FROM dbo.Cfg_ValueType v
     WHERE v.CompanyID = @CompanyID
        OR (v.CompanyID = 0 AND NOT EXISTS (SELECT 1 FROM dbo.Cfg_ValueType c
                                             WHERE c.CompanyID = @CompanyID AND c.Code = v.Code))
     ORDER BY v.SortOrder, v.Code;

    -- 3. features / business rules
    SELECT f.FeatureKey, f.FeatureValue, f.IsEnabled
      FROM dbo.Cfg_Feature f
     WHERE f.CompanyID = @CompanyID
        OR (f.CompanyID = 0 AND NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature c
                                             WHERE c.CompanyID = @CompanyID AND c.FeatureKey = f.FeatureKey));

    -- 4. form fields - each value: company, else All companies, else NULL (= as today)
    SELECT COALESCE(c.FormKey, d.FormKey)         AS FormKey,
           COALESCE(c.FieldKey, d.FieldKey)       AS FieldKey,
           COALESCE(c.IsVisible, d.IsVisible)     AS IsVisible,
           COALESCE(c.IsRequired, d.IsRequired)   AS IsRequired,
           COALESCE(c.IsReadOnly, d.IsReadOnly)   AS IsReadOnly,
           COALESCE(c.MinLength, d.MinLength)     AS MinLength,
           COALESCE(c.MaxLength, d.MaxLength)     AS MaxLength,
           COALESCE(c.Pattern, d.Pattern)         AS Pattern,
           COALESCE(c.LabelText, d.LabelText)     AS LabelText,
           COALESCE(c.Placeholder, d.Placeholder) AS Placeholder,
           COALESCE(c.HelpText, d.HelpText)       AS HelpText,
           COALESCE(c.SortOrder, d.SortOrder)     AS SortOrder
      FROM (SELECT * FROM dbo.Cfg_FormField WHERE CompanyID = @CompanyID) c
      FULL OUTER JOIN (SELECT * FROM dbo.Cfg_FormField WHERE CompanyID = 0 AND @CompanyID <> 0) d
        ON d.FormKey = c.FormKey AND d.FieldKey = c.FieldKey;

    -- 5. branding - each value: company, else All companies, else NULL (= as today)
    SELECT COALESCE(c.LogoUrl, d.LogoUrl)             AS LogoUrl,
           COALESCE(c.FaviconUrl, d.FaviconUrl)       AS FaviconUrl,
           COALESCE(c.PrimaryColor, d.PrimaryColor)   AS PrimaryColor,
           COALESCE(c.SecondaryColor, d.SecondaryColor) AS SecondaryColor,
           COALESCE(c.LoginBgUrl, d.LoginBgUrl)       AS LoginBgUrl,
           COALESCE(c.LoginTagline, d.LoginTagline)   AS LoginTagline,
           COALESCE(c.FooterText, d.FooterText)       AS FooterText,
           COALESCE(c.CopyrightText, d.CopyrightText) AS CopyrightText,
           COALESCE(c.ThemeJson, d.ThemeJson)         AS ThemeJson
      FROM (SELECT * FROM dbo.Cfg_Branding WHERE CompanyID = @CompanyID) c
      FULL OUTER JOIN (SELECT * FROM dbo.Cfg_Branding WHERE CompanyID = 0 AND @CompanyID <> 0) d
        ON 1 = 1;

    -- 6. version: company + All companies (0 = nothing configured yet)
    SELECT ISNULL((SELECT Version FROM dbo.Cfg_Version WHERE CompanyID = @CompanyID), 0)
         + CASE WHEN @CompanyID = 0 THEN 0
                ELSE ISNULL((SELECT Version FROM dbo.Cfg_Version WHERE CompanyID = 0), 0) END AS Version;
END
GO
