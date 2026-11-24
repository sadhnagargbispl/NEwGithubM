/* 16_seed_admin_review_fixes.sql (2026-11-20) - from the Admin code review.
   AddBVPointTS: GitHub shows "SP Type:" / "SP Point:" for company 1112 (default "BV Type" / "BV Point").
   Run on the MASTER. Safe to run again. */
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Label WHERE CompanyID = 1112 AND LabelKey = 'addbvpointts.type_caption') INSERT dbo.Cfg_Label (CompanyID, LabelKey, LabelText, UpdatedBy) VALUES (1112, 'addbvpointts.type_caption', N'SP Type', N'16_seed (from code)');
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Label WHERE CompanyID = 1112 AND LabelKey = 'addbvpointts.point_caption') INSERT dbo.Cfg_Label (CompanyID, LabelKey, LabelText, UpdatedBy) VALUES (1112, 'addbvpointts.point_caption', N'SP Point', N'16_seed (from code)');
EXEC dbo.Cfg_Touch @CompanyID = 1112;
-- DispatchStatus: GitHub condition includes 1111 (added by the developers after the first seed).
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1111 AND FeatureKey = 'dispatchstatus.log_and_update_purchase_req') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1111, 'dispatchstatus.log_and_update_purchase_req', N'1', 1, N'16_seed (from code)', GETDATE());
EXEC dbo.Cfg_Touch @CompanyID = 1111;
