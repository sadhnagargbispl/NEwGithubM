# Removing company IDs from the Admin panel (VB.NET) - 2026-11-18

Owner's rule: **no company ID may stay written in panel code**; every company-specific behaviour comes
from Super Admin settings (data), and **every company must behave exactly like the developers' latest
GitHub code**. Read the C# version first for the method: `REMOVE-COMPANY-IDS.md` (same folder). This file
gives the VB differences and the GitHub-match rule.

Site: `D:\Sadhna\MLMSaas\Basic-MLM-ADMIN` - ASP.NET Web Forms, **VB 9 / .NET 3.5** (no `$"..."`, no `?.`,
no multi-line lambdas; continue lines with ` _`). Helper class `PanelConfig` in `App_Code\clsPanelConfig.vb`:
`IsOn(key, default)`, `Text`, `Number` (Decimal, literal like `500D`), `Label`, `ValueName`.

## Where the work is
- `D:\Sadhna\SaWork\cidtool\out-admin2\remaining.txt` - every live line with a company ID next to a company read (file:line).
- `D:\Sadhna\SaWork\cidtool\out-admin2\report.json` - `PanelConfig.X("key", <expr>)` calls whose default still contains
  a company condition (Status "manual"; `Note`, `Expr`). 90 of them are "same key, different company lists in
  different places": give each distinct meaning its own key (e.g. `key_2`) or one key if the code really does the same thing.
- Also look for checks the scan misses: `Select Case Session("CompID")`, `IIf(...)`, `compid` variables, `In (...)` lists.

## VB patterns
```vb
If Session("CompID") = "1109" Or Session("CompID") = "1113" Then      ->  If PanelConfig.IsOn("page.what", False) Then      (seed 1109, 1113 = '1')
If Session("CompID") <> "1114" Then                                    ->  If PanelConfig.IsOn("page.what", True) Then       (seed 1114 = '0')
Select Case Session("CompID") / Case "1106": x = "a" / Case Else: x = "c"  ->  x = PanelConfig.Text("page.what", "c")    (seed 1106 = a)
  (if the Case branches run different CODE: one IsOn switch per branch, keep the order)
Session("CompID") = 1010 (number, unquoted) - same, it is still a company check.
Markup: <% If PanelConfig.IsOn("page.what", False) Then %> ... <% End If %>
```
Text/label in markup: `<%= Server.HtmlEncode(PanelConfig.Label("key", "Today's text")) %>`.
Company IDs used as DATA (SP name suffix, folder, URL): `PanelConfig.Text` with the generic value; seed others.
Secrets (passwords, API keys, tokens) are never copied into seeds/catalogs - switch only, list under "secrets".

## The GitHub-match rule (files that came from / were merged with GitHub)
`D:\Sadhna\SaWork\merge-admin\merge-report.txt` lists files merged today (MERGED-CLEAN, CONFLICTS, TAKE-THEIRS, NEW-IN-GITHUB).
For those files the truth is git HEAD of `D:\Sadhna\New Github\Basic-MLM-ADMIN`
(`"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe" -c safe.directory=* -C "D:\Sadhna\New Github\Basic-MLM-ADMIN" show HEAD:<path>` - read only).
For every setting call in such a file, compare, per company, the GitHub condition with what the panel will do
(generic default in code + seed rows already in the master DB). Fix differences:
- company missing → add a seed row;
- company GitHub removed but a seed row (UpdatedBy like '%seed%') still exists → add a DELETE for it to your SQL file;
- rows whose UpdatedBy is a Super Admin user are owner choices: never change, just report.
Master DB to read current rows: SQL Server 43.242.226.223,1533, database mlmmenumaster-test, login usrmlmmenutst,
password = connection "sconstr" in `D:\Sadhna\MLMSaas\Basic-MLM-ADMIN\web.config` (never print it). Tables
Cfg_Feature(CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy), Cfg_Label(CompanyID, LabelKey, LabelText, UpdatedBy).
IsOn semantics: company row exists ? IsEnabled : default. READ ONLY - do not run your SQL file.

Known items from the merge (the merge agents listed them):
- THEIRS adds 1112 to reward.self_team_search, reward.rank_reward_caption, reward.hide_rank_type_column, reward.show_reward_name_column, statementweekly.matched_bv_each_leg;
  1111+1112 to viewreferralincome.green_layout and viewlevelincome.alternate_grid; 1114 to idactivate.activate_with_bill_no;
  1112 shows value name BV as "SP" (AddBVPointTS, AdminHome "Package S.P") - use ValueName seed for code BV = "SP" for 1112 if consistent.
- THEIRS removed: 1038 statementweekly.level_income_grid_only branch; 1039 wallettransactionreport.detail_sp_new; home.wallet_summary now default True.
  Make the data match (delete stale seed rows where GitHub dropped a company).

## Outputs (per group)
- edited source files (only your pages; .aspx + .aspx.vb of a page belong together);
- `D:\Sadhna\MLMSaas\SuperAdminConfig\14_seed_admin_<group>.sql` - inserts like the 09_/11_ files with UpdatedBy N'14_seed (from code)',
  plus `DELETE FROM dbo.Cfg_Feature WHERE CompanyID = X AND FeatureKey = 'k' AND UpdatedBy LIKE '%seed%';` lines where needed. Start with SET NOCOUNT ON;
- `D:\Sadhna\MLMSaas\SuperAdminConfig\catalog\admin-cid-<group>.json` - new keys (panel "admin"), skipped, secrets.
No backups (owner's rule). Keep encoding/BOM/CRLF. Do not compile the whole site (others edit in parallel) - re-read every edit for VB 9 syntax.

## Final message
Counts (spots changed, keys new/reused, seed inserts/deletes), every company-ID literal left with reason, GitHub-match mismatches found and fixed, anything for the owner.
