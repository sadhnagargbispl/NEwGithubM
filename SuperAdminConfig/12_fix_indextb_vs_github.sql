-- 12_fix_indextb_vs_github.sql
-- Member dashboard (cpanel Indextb) made to match the developers' GitHub HEAD (commit 9467be5).
-- In 9467be5 company 1115 was taken out of the "hide referral link boxes" list and the
-- "profile with summaries" layout list, and added to the "news 1/3 width with business" list.
-- Only seed rows are removed; the new row is marked 12_fix (from GitHub).
SET NOCOUNT ON;

DELETE FROM dbo.Cfg_Feature
 WHERE CompanyID = 1115
   AND FeatureKey IN ('indextb.hide_referral_link_boxes', 'indextb.profile_with_summaries_layout')
   AND UpdatedBy LIKE '%seed%';

IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1115 AND FeatureKey = 'indextb.news_third_with_business')
    INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt)
    VALUES (1115, 'indextb.news_third_with_business', N'1', 1, N'12_fix (from GitHub)', GETDATE());
ELSE
    UPDATE dbo.Cfg_Feature SET FeatureValue = N'1', IsEnabled = 1, UpdatedBy = N'12_fix (from GitHub)', UpdatedAt = GETDATE()
     WHERE CompanyID = 1115 AND FeatureKey = 'indextb.news_third_with_business' AND UpdatedBy LIKE '%seed%';

EXEC dbo.Cfg_Touch @CompanyID = 1115;

-- Tell the panels to re-read: bump Cfg_Version for every company that has settings rows (Cfg_Touch inserts the row when missing).
DECLARE @touch_c int;
DECLARE touch_cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT CompanyID FROM dbo.Cfg_Feature UNION SELECT CompanyID FROM dbo.Cfg_Label;
OPEN touch_cur; FETCH NEXT FROM touch_cur INTO @touch_c;
WHILE @@FETCH_STATUS = 0 BEGIN EXEC dbo.Cfg_Touch @CompanyID = @touch_c; FETCH NEXT FROM touch_cur INTO @touch_c; END;
CLOSE touch_cur; DEALLOCATE touch_cur;