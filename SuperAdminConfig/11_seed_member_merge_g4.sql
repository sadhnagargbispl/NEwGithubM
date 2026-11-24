SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1113 AND FeatureKey = 'groupdirects.total_in_update_panel') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1113, 'groupdirects.total_in_update_panel', N'1', 1, N'11_seed (from code)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1113 AND FeatureKey = 'indextb.directs_view_link') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1113, 'indextb.directs_view_link', N'1', 1, N'11_seed (from code)', GETDATE());

-- Tell the panels to re-read: bump Cfg_Version for every company that has settings rows (Cfg_Touch inserts the row when missing).
DECLARE @touch_c int;
DECLARE touch_cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT CompanyID FROM dbo.Cfg_Feature UNION SELECT CompanyID FROM dbo.Cfg_Label;
OPEN touch_cur; FETCH NEXT FROM touch_cur INTO @touch_c;
WHILE @@FETCH_STATUS = 0 BEGIN EXEC dbo.Cfg_Touch @CompanyID = @touch_c; FETCH NEXT FROM touch_cur INTO @touch_c; END;
CLOSE touch_cur; DEALLOCATE touch_cur;