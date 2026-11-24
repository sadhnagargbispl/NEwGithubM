/* =============================================================================
   Super Admin - central configuration store for the 5 panels
   (Admin, Member cPanel, Franchise, Shoppy, Web)

   RUN ON: the MASTER database - the same database the panels' "sconstr"
           connection points to (the one that holds M_CompanyMasterNew and
           M_CompWiseWebMenuMaster).

   SAFE TO RUN:
     - Creates NEW tables and procedures only. No existing table, column,
       procedure or row is changed or deleted.
     - Can be run again any time; whatever already exists is skipped.
     - Empty tables = every panel works and looks exactly as today. A panel
       only changes for a company once something is saved for that company
       from Super Admin.

   Every table is keyed by CompanyID = M_CompanyMasterNew.ID.
   CompanyID 0 = a default for ALL companies (a company's own row wins).
   ============================================================================= */

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------------------
   1. Cfg_Label - every word a panel shows (labels, headings, buttons, messages)
      e.g. LabelKey 'term.sponsor' -> 'Introducer', 'term.leg1' -> 'Group A'
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.Cfg_Label', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cfg_Label (
        CompanyID   INT            NOT NULL,
        LabelKey    VARCHAR(150)   NOT NULL,
        LabelText   NVARCHAR(500)  NOT NULL,
        UpdatedBy   NVARCHAR(100)  NULL,
        UpdatedAt   DATETIME       NOT NULL CONSTRAINT DF_Cfg_Label_UpdatedAt DEFAULT (GETDATE()),
        CONSTRAINT PK_Cfg_Label PRIMARY KEY (CompanyID, LabelKey)
    );
END
GO

/* ---------------------------------------------------------------------------
   2. Cfg_ValueType - names of the value units (BV, PV, SP, RB, SB, EV ...)
      Code never changes (the database and income procedures use it);
      DisplayName is what the panels show.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.Cfg_ValueType', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cfg_ValueType (
        CompanyID    INT            NOT NULL,
        Code         VARCHAR(10)    NOT NULL,
        DisplayName  NVARCHAR(60)   NOT NULL,
        IsEnabled    BIT            NOT NULL CONSTRAINT DF_Cfg_ValueType_IsEnabled DEFAULT (1),
        SortOrder    INT            NOT NULL CONSTRAINT DF_Cfg_ValueType_SortOrder DEFAULT (0),
        UpdatedBy    NVARCHAR(100)  NULL,
        UpdatedAt    DATETIME       NOT NULL CONSTRAINT DF_Cfg_ValueType_UpdatedAt DEFAULT (GETDATE()),
        CONSTRAINT PK_Cfg_ValueType PRIMARY KEY (CompanyID, Code)
    );
END
GO

/* ---------------------------------------------------------------------------
   3. Cfg_Feature - on/off switches and business rules that are today written
      as "If CompID = 1109 ..." in code.
      e.g. 'withdraw.min_amount' = '500', 'withdraw.tds_percent' = '2',
           'withdraw.kyc_required' = '1', 'register.pan_required' = '1'
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.Cfg_Feature', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cfg_Feature (
        CompanyID    INT            NOT NULL,
        FeatureKey   VARCHAR(100)   NOT NULL,
        FeatureValue NVARCHAR(1000) NULL,
        IsEnabled    BIT            NOT NULL CONSTRAINT DF_Cfg_Feature_IsEnabled DEFAULT (1),
        UpdatedBy    NVARCHAR(100)  NULL,
        UpdatedAt    DATETIME       NOT NULL CONSTRAINT DF_Cfg_Feature_UpdatedAt DEFAULT (GETDATE()),
        CONSTRAINT PK_Cfg_Feature PRIMARY KEY (CompanyID, FeatureKey)
    );
END
GO

/* ---------------------------------------------------------------------------
   4. Cfg_FormField - registration / profile / KYC form rules per company
      FormKey: 'register', 'profile', 'kyc', 'franchise_register' ...
      FieldKey: 'father_name', 'pincode', 'email', 'pan', 'mobile', 'dob' ...
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.Cfg_FormField', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cfg_FormField (
        CompanyID    INT            NOT NULL,
        FormKey      VARCHAR(50)    NOT NULL,
        FieldKey     VARCHAR(80)    NOT NULL,
        IsVisible    BIT            NULL,      -- NULL = as today (the panel decides)
        IsRequired   BIT            NULL,      -- NULL = as today
        IsReadOnly   BIT            NULL,      -- NULL = as today
        MinLength    INT            NULL,
        MaxLength    INT            NULL,
        Pattern      NVARCHAR(300)  NULL,      -- e.g. ^[1-9][0-9]{5}$ for pincode
        LabelText    NVARCHAR(200)  NULL,      -- NULL = the panel's own label
        Placeholder  NVARCHAR(200)  NULL,
        HelpText     NVARCHAR(500)  NULL,
        SortOrder    INT            NULL,      -- NULL = the panel's own order
        UpdatedBy    NVARCHAR(100)  NULL,
        UpdatedAt    DATETIME       NOT NULL CONSTRAINT DF_Cfg_FormField_UpdatedAt DEFAULT (GETDATE()),
        CONSTRAINT PK_Cfg_FormField PRIMARY KEY (CompanyID, FormKey, FieldKey)
    );
END
GO

/* ---------------------------------------------------------------------------
   5. Cfg_Branding - logo, colours, login page per company (NULL = as today)
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.Cfg_Branding', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cfg_Branding (
        CompanyID     INT            NOT NULL CONSTRAINT PK_Cfg_Branding PRIMARY KEY,
        LogoUrl       NVARCHAR(500)  NULL,
        FaviconUrl    NVARCHAR(500)  NULL,
        PrimaryColor  VARCHAR(20)    NULL,     -- e.g. #1860A8
        SecondaryColor VARCHAR(20)   NULL,
        LoginBgUrl    NVARCHAR(500)  NULL,
        LoginTagline  NVARCHAR(300)  NULL,
        FooterText    NVARCHAR(500)  NULL,
        CopyrightText NVARCHAR(300)  NULL,
        ThemeJson     NVARCHAR(MAX)  NULL,     -- extra colour tokens, optional
        UpdatedBy     NVARCHAR(100)  NULL,
        UpdatedAt     DATETIME       NOT NULL CONSTRAINT DF_Cfg_Branding_UpdatedAt DEFAULT (GETDATE())
    );
END
GO

/* ---------------------------------------------------------------------------
   6. Cfg_PageRight - action rights per company, panel, user group and page.
      Today a group either sees a menu or not; this adds View/Add/Edit/
      Delete/Approve/Export per page. No row = today's behaviour.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.Cfg_PageRight', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cfg_PageRight (
        CompanyID    INT            NOT NULL,
        Panel        VARCHAR(20)    NOT NULL,  -- admin | member | franchise | shoppy | web
        GroupID      INT            NOT NULL,  -- M_UserGroupMaster.GroupId (admin panel)
        PageKey      VARCHAR(150)   NOT NULL,  -- page name, e.g. 'FundWithdraw.aspx'
        CanView      BIT            NOT NULL CONSTRAINT DF_Cfg_PageRight_CanView    DEFAULT (1),
        CanAdd       BIT            NOT NULL CONSTRAINT DF_Cfg_PageRight_CanAdd     DEFAULT (1),
        CanEdit      BIT            NOT NULL CONSTRAINT DF_Cfg_PageRight_CanEdit    DEFAULT (1),
        CanDelete    BIT            NOT NULL CONSTRAINT DF_Cfg_PageRight_CanDelete  DEFAULT (1),
        CanApprove   BIT            NOT NULL CONSTRAINT DF_Cfg_PageRight_CanApprove DEFAULT (1),
        CanExport    BIT            NOT NULL CONSTRAINT DF_Cfg_PageRight_CanExport  DEFAULT (1),
        UpdatedBy    NVARCHAR(100)  NULL,
        UpdatedAt    DATETIME       NOT NULL CONSTRAINT DF_Cfg_PageRight_UpdatedAt DEFAULT (GETDATE()),
        CONSTRAINT PK_Cfg_PageRight PRIMARY KEY (CompanyID, Panel, GroupID, PageKey)
    );
END
GO

/* ---------------------------------------------------------------------------
   7. Cfg_ChangeLog - who changed what, before and after (for history + undo)
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.Cfg_ChangeLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cfg_ChangeLog (
        LogID       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Cfg_ChangeLog PRIMARY KEY,
        CompanyID   INT            NOT NULL,
        Area        VARCHAR(50)    NOT NULL,   -- label | valuetype | feature | formfield | branding | right | menu
        ItemKey     NVARCHAR(300)  NOT NULL,
        Action      VARCHAR(20)    NOT NULL,   -- insert | update | delete | undo
        OldValue    NVARCHAR(MAX)  NULL,
        NewValue    NVARCHAR(MAX)  NULL,
        ChangedBy   NVARCHAR(100)  NULL,
        ChangedAt   DATETIME       NOT NULL CONSTRAINT DF_Cfg_ChangeLog_ChangedAt DEFAULT (GETDATE())
    );
    CREATE INDEX IX_Cfg_ChangeLog_Company ON dbo.Cfg_ChangeLog (CompanyID, ChangedAt DESC);
END
GO

/* ---------------------------------------------------------------------------
   8. Cfg_Version - one number per company, raised on every Super Admin save.
      Panels keep the company's config in memory and re-read it only when
      this number changes - one tiny query, no slow-down.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.Cfg_Version', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cfg_Version (
        CompanyID   INT       NOT NULL CONSTRAINT PK_Cfg_Version PRIMARY KEY,
        Version     INT       NOT NULL CONSTRAINT DF_Cfg_Version_Version DEFAULT (1),
        UpdatedAt   DATETIME  NOT NULL CONSTRAINT DF_Cfg_Version_UpdatedAt DEFAULT (GETDATE())
    );
END
GO

/* ===========================================================================
   PROCEDURES
   =========================================================================== */

/* Cfg_GetCompanyConfig @CompanyID
   Everything a panel needs for one company, in one call, 6 result sets:
     1 labels, 2 value types, 3 features, 4 form fields, 5 branding, 6 version
   A company's own row wins over the CompanyID 0 default; for form fields and
   branding each VALUE is combined (company, else All companies, else NULL). */
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

/* Cfg_GetVersion @CompanyID - the cheap check panels make before re-reading. */
IF OBJECT_ID('dbo.Cfg_GetVersion', 'P') IS NULL
    EXEC ('CREATE PROCEDURE dbo.Cfg_GetVersion AS SELECT 1;');
GO
ALTER PROCEDURE dbo.Cfg_GetVersion
    @CompanyID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ISNULL((SELECT Version FROM dbo.Cfg_Version WHERE CompanyID = @CompanyID), 0)
         + ISNULL((SELECT Version FROM dbo.Cfg_Version WHERE CompanyID = 0), 0) AS Version;
END
GO

/* Cfg_Touch @CompanyID - Super Admin calls this after every save, so panels
   pick the change up on their next request. */
IF OBJECT_ID('dbo.Cfg_Touch', 'P') IS NULL
    EXEC ('CREATE PROCEDURE dbo.Cfg_Touch AS SELECT 1;');
GO
ALTER PROCEDURE dbo.Cfg_Touch
    @CompanyID INT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.Cfg_Version WHERE CompanyID = @CompanyID)
        UPDATE dbo.Cfg_Version SET Version = Version + 1, UpdatedAt = GETDATE() WHERE CompanyID = @CompanyID;
    ELSE
        INSERT INTO dbo.Cfg_Version (CompanyID, Version) VALUES (@CompanyID, 1);
END
GO

/* ---------------------------------------------------------------------------
   CHECK - after running, this should list the 8 tables and 3 procedures.
   --------------------------------------------------------------------------- */
SELECT name, type_desc
  FROM sys.objects
 WHERE name IN ('Cfg_Label', 'Cfg_ValueType', 'Cfg_Feature', 'Cfg_FormField', 'Cfg_Branding',
                'Cfg_PageRight', 'Cfg_ChangeLog', 'Cfg_Version',
                'Cfg_GetCompanyConfig', 'Cfg_GetVersion', 'Cfg_Touch')
 ORDER BY type_desc, name;
GO
