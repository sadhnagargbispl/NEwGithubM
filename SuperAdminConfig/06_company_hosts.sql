/* =====================================================================
   06_company_hosts.sql  -  one website address PER PANEL for a company
   ---------------------------------------------------------------------
   Run on the MASTER database (e.g. mlmmenumaster-test). Safe to run again.

   Why: the panels find the company by the website they are opened on
   (M_CompanyMasterNew.URL, after cutting "cpanel." / "admin." etc.).
   That only works when member and admin share one name, e.g.
       cpanel.company.com  /  admin.company.com   ->  URL = company.com
   A company that uses unrelated names, e.g.
       aventureuser.bisplindia.in  (member)  /  aventureadmin.bisplindia.in  (admin)
   lists each name here. The panels then use the company's URL for it.
   A company with no rows here works exactly as before.

   Panel = 'member' | 'admin' | 'website'
   ===================================================================== */
SET NOCOUNT ON;

IF OBJECT_ID('dbo.Cfg_CompanyHost', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cfg_CompanyHost
    (
        HostID      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Cfg_CompanyHost PRIMARY KEY,
        CompanyID   int            NOT NULL,
        Panel       varchar(20)    NOT NULL,
        Host        nvarchar(200)  NOT NULL,   -- lower case, no http://, no path, no port
        IsActive    bit            NOT NULL CONSTRAINT DF_Cfg_CompanyHost_IsActive DEFAULT (1),
        AddDate     datetime       NOT NULL CONSTRAINT DF_Cfg_CompanyHost_AddDate DEFAULT (GETDATE()),
        AddedBy     nvarchar(100)  NULL,
        CONSTRAINT CK_Cfg_CompanyHost_Panel CHECK (Panel IN ('member', 'admin', 'website'))
    );
    -- one address belongs to one company only
    CREATE UNIQUE INDEX UX_Cfg_CompanyHost_Host ON dbo.Cfg_CompanyHost (Host);
    CREATE INDEX IX_Cfg_CompanyHost_Company ON dbo.Cfg_CompanyHost (CompanyID, Panel);
END
GO
