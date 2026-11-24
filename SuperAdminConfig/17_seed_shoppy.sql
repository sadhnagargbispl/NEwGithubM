/* 17_seed_shoppy.sql - Shoppy panel (Pulstiya_Shoppy / VitaFlow.Presenation, ASP.NET Core), 2026-11-20.
   The Shoppy code has NO company-ID checks. Company values that were written in the code (another company's
   member-API address, default product image addresses) and the per-deployment words/addresses from
   appsettings.json now go through PanelConfig with today's value as the default in code.
   These rows record today's values for the company this Shoppy is deployed for (appsettings CompID = 1112,
   Pulastya Globals: database pulstya, domains *.pulastyaglobals.in), so Super Admin shows and can change them.
   Nothing changes on screen: every value below is exactly what the code/appsettings use today.
   Secrets (the member-API "logkey", database passwords) are NOT copied here.
   Run on the MASTER. Safe to run again: a value that already exists (e.g. set in Super Admin) is never overwritten. */
SET NOCOUNT ON;
-- Member API used for member login / balance / PV-BV posting (today: another company's address, written in TransactionRepository.cs).
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1112 AND FeatureKey = 'shoppy.transactionrepository.member_api_url') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1112, 'shoppy.transactionrepository.member_api_url', N'https://zoewellness.co.in/Api/api.ashx', 1, N'17_seed (from code)', GETDATE());
-- Fallback product pictures.
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1112 AND FeatureKey = 'shoppy.checkout.default_product_image') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1112, 'shoppy.checkout.default_product_image', N'https://zadmin.zoewellness.co.in/images/DefaultProduct.jpg', 1, N'17_seed (from code)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1112 AND FeatureKey = 'shoppy.productlist.default_product_image') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1112, 'shoppy.productlist.default_product_image', N'https://franchise.expertholistic.com/images/DefaultProduct.jpg', 1, N'17_seed (from code)', GETDATE());
-- Addresses from appsettings.json.
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1112 AND FeatureKey = 'shoppy.appsettings.shopping_url') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1112, 'shoppy.appsettings.shopping_url', N'https://shop.pulastyaglobals.in/WalletReqsImages/', 1, N'17_seed (from code)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1112 AND FeatureKey = 'shoppy.appsettings.franchise_url') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1112, 'shoppy.appsettings.franchise_url', N'https://franchise.pulastyaglobals.in/', 1, N'17_seed (from code)', GETDATE());
-- Words from appsettings.json.
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Label WHERE CompanyID = 1112 AND LabelKey = 'shoppy.appsettings.cv_caption') INSERT dbo.Cfg_Label (CompanyID, LabelKey, LabelText, UpdatedBy) VALUES (1112, 'shoppy.appsettings.cv_caption', N'C.V.', N'17_seed (from code)');
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Label WHERE CompanyID = 1112 AND LabelKey = 'shoppy.appsettings.bv_caption') INSERT dbo.Cfg_Label (CompanyID, LabelKey, LabelText, UpdatedBy) VALUES (1112, 'shoppy.appsettings.bv_caption', N'S.P.', N'17_seed (from code)');
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Label WHERE CompanyID = 1112 AND LabelKey = 'shoppy.appsettings.pv_caption') INSERT dbo.Cfg_Label (CompanyID, LabelKey, LabelText, UpdatedBy) VALUES (1112, 'shoppy.appsettings.pv_caption', N'P.V.', N'17_seed (from code)');
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Label WHERE CompanyID = 1112 AND LabelKey = 'shoppy.appsettings.rp_caption') INSERT dbo.Cfg_Label (CompanyID, LabelKey, LabelText, UpdatedBy) VALUES (1112, 'shoppy.appsettings.rp_caption', N'R.P.', N'17_seed (from code)');
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Label WHERE CompanyID = 1112 AND LabelKey = 'shoppy.appsettings.customer_caption') INSERT dbo.Cfg_Label (CompanyID, LabelKey, LabelText, UpdatedBy) VALUES (1112, 'shoppy.appsettings.customer_caption', N'Consultant (PC)', N'17_seed (from code)');
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Label WHERE CompanyID = 1112 AND LabelKey = 'shoppy.appsettings.party_caption') INSERT dbo.Cfg_Label (CompanyID, LabelKey, LabelText, UpdatedBy) VALUES (1112, 'shoppy.appsettings.party_caption', N'Shoppe', N'17_seed (from code)');
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Label WHERE CompanyID = 1112 AND LabelKey = 'shoppy.appsettings.repurchase_caption') INSERT dbo.Cfg_Label (CompanyID, LabelKey, LabelText, UpdatedBy) VALUES (1112, 'shoppy.appsettings.repurchase_caption', N'Repurchase Invoice', N'17_seed (from code)');
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Label WHERE CompanyID = 1112 AND LabelKey = 'shoppy.appsettings.joining_caption') INSERT dbo.Cfg_Label (CompanyID, LabelKey, LabelText, UpdatedBy) VALUES (1112, 'shoppy.appsettings.joining_caption', N'Activation Invoice', N'17_seed (from code)');
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Label WHERE CompanyID = 1112 AND LabelKey = 'shoppy.appsettings.barcode_caption') INSERT dbo.Cfg_Label (CompanyID, LabelKey, LabelText, UpdatedBy) VALUES (1112, 'shoppy.appsettings.barcode_caption', N'Barcode', N'17_seed (from code)');
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Label WHERE CompanyID = 1112 AND LabelKey = 'shoppy.appsettings.shoppe_caption') INSERT dbo.Cfg_Label (CompanyID, LabelKey, LabelText, UpdatedBy) VALUES (1112, 'shoppy.appsettings.shoppe_caption', N'Shoppe', N'17_seed (from code)');

-- Tell the panels to re-read: bump Cfg_Version for every company that has settings rows (Cfg_Touch inserts the row when missing).
DECLARE @touch_c int;
DECLARE touch_cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT CompanyID FROM dbo.Cfg_Feature UNION SELECT CompanyID FROM dbo.Cfg_Label;
OPEN touch_cur; FETCH NEXT FROM touch_cur INTO @touch_c;
WHILE @@FETCH_STATUS = 0 BEGIN EXEC dbo.Cfg_Touch @CompanyID = @touch_c; FETCH NEXT FROM touch_cur INTO @touch_c; END;
CLOSE touch_cur; DEALLOCATE touch_cur;
