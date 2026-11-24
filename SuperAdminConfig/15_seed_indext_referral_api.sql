/* 15_seed_indext_referral_api.sql (2026-11-18)
   IndexT dashboard: the referral-link API call (sends the member's ID + password to cpanel.myhemalika.com)
   now runs only where switched on - owner's decision; same as IndexGreen (indexgreen.referral_link_from_api = 1106).
   Run on the MASTER. Safe to run again. */
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1106 AND FeatureKey = 'indext.referral_link_from_api') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1106, 'indext.referral_link_from_api', N'1', 1, N'15_seed (owner decision)', GETDATE());
EXEC dbo.Cfg_Touch @CompanyID = 1106;
