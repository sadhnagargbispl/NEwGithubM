SET NOCOUNT ON;
-- 17_seed_franchise: company-ID removal, Franchise panel (Franchise_Pulastyaglobals, GitHub 2026-11-19), 2026-11-20. Generated from code; do not edit by hand.
-- Defaults in code are the generic behaviour; these rows keep today's behaviour for the companies the old code named.
-- All seven keys sit on views no controller renders today (see catalog\franchise.json). Text settings in catalog\franchise-settings.json need no seed (code default = today's value).
-- Run on the MASTER. Safe to run again.
-- features
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1006 AND FeatureKey = 'franchise.dispatchorder1.show_paymode') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1006, 'franchise.dispatchorder1.show_paymode', N'1', 1, N'17_seed (from code)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1055 AND FeatureKey = 'franchise.productmaster1030.show_product_for') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1055, 'franchise.productmaster1030.show_product_for', N'0', 0, N'17_seed (from code)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1007 AND FeatureKey = 'franchise.invoiceprint_copy.gstin_shows_aadhar') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1007, 'franchise.invoiceprint_copy.gstin_shows_aadhar', N'1', 1, N'17_seed (from code)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1007 AND FeatureKey = 'franchise.invoiceprint_copy.vadic_terms') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1007, 'franchise.invoiceprint_copy.vadic_terms', N'1', 1, N'17_seed (from code)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1038 AND FeatureKey = 'franchise.invoiceprint_copy.return_within_30_days') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1038, 'franchise.invoiceprint_copy.return_within_30_days', N'1', 1, N'17_seed (from code)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1006 AND FeatureKey = 'franchise.distributorbillvadic.show_bill_type_message') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1006, 'franchise.distributorbillvadic.show_bill_type_message', N'1', 1, N'17_seed (from code)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1007 AND FeatureKey = 'franchise.distributorbillvadic.show_bill_type_message') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1007, 'franchise.distributorbillvadic.show_bill_type_message', N'1', 1, N'17_seed (from code)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1006 AND FeatureKey = 'franchise.distributorbillggs.show_bill_type_message') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1006, 'franchise.distributorbillggs.show_bill_type_message', N'1', 1, N'17_seed (from code)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1007 AND FeatureKey = 'franchise.distributorbillggs.show_bill_type_message') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1007, 'franchise.distributorbillggs.show_bill_type_message', N'1', 1, N'17_seed (from code)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1020 AND FeatureKey = 'franchise.distributorbillggs.show_bill_type_message') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1020, 'franchise.distributorbillggs.show_bill_type_message', N'1', 1, N'17_seed (from code)', GETDATE());

-- Tell the panels to re-read: bump Cfg_Version for every company that has settings rows (Cfg_Touch inserts the row when missing).
DECLARE @touch_c int;
DECLARE touch_cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT CompanyID FROM dbo.Cfg_Feature UNION SELECT CompanyID FROM dbo.Cfg_Label;
OPEN touch_cur; FETCH NEXT FROM touch_cur INTO @touch_c;
WHILE @@FETCH_STATUS = 0 BEGIN EXEC dbo.Cfg_Touch @CompanyID = @touch_c; FETCH NEXT FROM touch_cur INTO @touch_c; END;
CLOSE touch_cur; DEALLOCATE touch_cur;
