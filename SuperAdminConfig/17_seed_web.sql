/* 17_seed_web.sql - Web panel (Web_Pulastyaglobals / Aarogyabhumi, ASP.NET MVC), 2026-11-20.
   Hardcoded company values removed from: Views/Shared/_Layout.cshtml (Franchise Login link),
   Repository/R_Login.cs (company number written into the WhatsApp request log).
   The code keeps today's value as its default, so these rows only make today's values explicit
   for Pulastya Globals (1112), the company this website is deployed for.
   Run on the MASTER. Safe to run again: a row is inserted only when it is missing. */
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1112 AND FeatureKey = 'web.layout.franchise_login_url') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1112, 'web.layout.franchise_login_url', N'https://shop.pulastyaglobals.in/', 1, N'17_seed (from code)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1112 AND FeatureKey = 'web.r_login.whatsapp_log_compid') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1112, 'web.r_login.whatsapp_log_compid', N'1007', 1, N'17_seed (from code)', GETDATE());

-- Tell the panels to re-read: bump Cfg_Version for every company that has settings rows (Cfg_Touch inserts the row when missing).
DECLARE @touch_c int;
DECLARE touch_cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT CompanyID FROM dbo.Cfg_Feature UNION SELECT CompanyID FROM dbo.Cfg_Label;
OPEN touch_cur; FETCH NEXT FROM touch_cur INTO @touch_c;
WHILE @@FETCH_STATUS = 0 BEGIN EXEC dbo.Cfg_Touch @CompanyID = @touch_c; FETCH NEXT FROM touch_cur INTO @touch_c; END;
CLOSE touch_cur; DEALLOCATE touch_cur;
