# Removing company IDs from the Member cPanel (2026-11-18)

Owner's rule: **no company ID may stay written in the panel code.** Every behaviour that
today depends on `CompID == "1109"`-style checks must come from Super Admin settings.
Today's behaviour for every company must stay exactly the same - it moves into DATA
(seed rows in the master), not into code.

Site: `D:\Sadhna\MLMSaas\cpanel.BasicmlmCSharp` (ASP.NET Web Forms, C#, compiled by
aspnet_compiler v4 - write **C# 5**: no `?.`, no `$"..."`, no `nameof`, no `=>` members, no pattern matching).

## What is already done
`cidtool` already converted 602 calls of the form `PanelConfig.IsOn("key", <company check>)`
to `PanelConfig.IsOn("key", false)` etc. and seeded the per-company values
(`SuperAdminConfig\08_seed_member_company_rules.sql`, already run on the test master).
What is left: lines that still contain a company ID literal ("10xx"/"11xx"), listed in
`D:\Sadhna\SaWork\cidtool\out\remaining.txt`, plus calls the tool could not convert, listed in
`SuperAdminConfig\catalog\member-cid-removed.report.json` (entries with `"Status": "manual"` - the
`Note` says why, `Expr` is the original default).

## The method (the same for every case)

1. Find what a company that is NOT named anywhere gets today = the **generic** behaviour
   (e.g. a new company like 1115). That becomes the default written in code.
2. For each company that is named and gets something different, write a **seed row**.
3. Replace the check in code by the setting call with the generic default. No company ID remains.

| Need | Call | Seed row value |
|---|---|---|
| yes/no | `PanelConfig.IsOn("key", false)` (or `true`) | `FeatureValue '1'/'0'`, `IsEnabled` = same 1/0 |
| text (URL, message, page name, SQL fragment that is NOT a secret) | `PanelConfig.Text("key", "generic text")` | `FeatureValue` = the text, `IsEnabled 1` (empty text cannot be stored - empty = not set) |
| amount / percent / count | `PanelConfig.Number("key", 300m)` | `FeatureValue` = number (invariant, e.g. `500` or `2.5`), `IsEnabled 1` |
| a word on screen | `PanelConfig.Label("key", "generic text")` | goes to `Cfg_Label` |

Patterns:
- `if (cid == "1109" || cid == "1113") {A} else {B}` -> `if (PanelConfig.IsOn("page.what", false)) {A} else {B}`; seed 1109 and 1113 with '1'.
- `if (cid != "1114") {A}` -> `if (PanelConfig.IsOn("page.what", true)) {A}`; seed 1114 with '0'.
- if/else-if chains or `switch (cid) { case "1106": x = "a"; break; case "1109": x = "b"; break; default: x = "c"; }`
  that pick a VALUE -> `x = PanelConfig.Text("page.what", "c");` seed 1106 = a, 1109 = b.
  If the branches run different CODE (not just a value), use one switch per branch:
  `if (PanelConfig.IsOn("page.flow_a", false)) {...} else if (PanelConfig.IsOn("page.flow_b", false)) {...} else {...}`.
  Keep the order of the old chain, so a company seeded for both behaves as before (it is only seeded for its own branch anyway).
- Mixed checks (`cid == "1109" && amount > 500`): only the company part becomes the setting:
  `PanelConfig.IsOn("page.what", false) && amount > 500`.
- Existing calls marked `manual` in the report (e.g. `PanelConfig.IsOn("k", cid == "1109" && x > 5)`):
  rewrite to `PanelConfig.IsOn("k", false) && x > 5` (keep the key if the meaning is the same).
  `"same key, different company lists in different places"`: give the places that differ their
  own key (e.g. `key_2`) and catalog + seed each.
- Markup `<% if (...) %>`, `'<%= ... %>' === "1"` JS checks, and data-bound `<%# %>` work the same way.
- Company IDs used as DATA, e.g. a company-specific connection, database name, API URL, folder name,
  redirect address: becomes a `Text` setting with the generic value as default, seeded per company.
- A literal that is NOT a company ID (an amount "1001", a year, a code) - leave it, and list it.

### Secrets
If a company branch contains a password, API key, token, merchant key or similar: do NOT copy the
secret into a seed or catalog. Turn the company check into a switch only (the secret stays where it
is), and list it under `"secrets"` in your catalog file (`where` + what it is, never the value).

## Keys
`<page>.<what>` - lower case `a-z 0-9 _ .`, max 100 chars, page = file name without extension
(e.g. `appapi.payout_from_wallet`). Before inventing a key, search all `SuperAdminConfig\catalog\*.json`
files for it - it must not already exist with another meaning. Same meaning in the same page = reuse one key.

## Your outputs
1. Edited source files (only the files given to you).
2. `SuperAdminConfig\catalog\member-cid-<group>.json`:
   ```json
   { "settings": [ { "key": "...", "panel": "member", "page": "AppApi.aspx", "kind": "switch|text|number|label",
                     "label": "plain English for the owner", "today": "On for 1102, 1109 - off for others",
                     "where": "AppApi.aspx.cs:3232, 3436" } ],
     "skipped": [ { "where": "file:line", "reason": "not a company id: amount 1001" } ],
     "secrets": [ { "where": "file:line", "what": "SMS password for 1109 - left in code" } ] }
   ```
   Every new key must be in it (keys you only reuse from earlier catalogs: do not repeat them).
3. `SuperAdminConfig\09_seed_member_cid_<group>.sql` - one line per row, exactly this form (do NOT run it):
   ```sql
   IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = 1109 AND FeatureKey = 'appapi.payout_from_wallet') INSERT dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy, UpdatedAt) VALUES (1109, 'appapi.payout_from_wallet', N'1', 1, N'09_seed (from code)', GETDATE());
   IF NOT EXISTS (SELECT 1 FROM dbo.Cfg_Label WHERE CompanyID = 1109 AND LabelKey = 'x.y') INSERT dbo.Cfg_Label (CompanyID, LabelKey, LabelText, UpdatedBy) VALUES (1109, 'x.y', N'Text', N'09_seed (from code)');
   ```
   Start the file with `SET NOCOUNT ON;`. Escape `'` as `''`.

## Do NOT
- make backup copies of files (owner's rule);
- touch `App_Code\PanelConfig.cs`, files not given to you, commented-out code, SQL text unrelated to the company check;
- reformat, re-indent, or change encoding / line endings;
- change behaviour for any company: for every company (named or not) the result must be the same as before
  once the seed rows are in;
- run aspnet_compiler on the whole site (others are editing at the same time) - instead re-read every
  edited spot and check brackets, semicolons and C# 5 syntax carefully. The lead compiles at the end.

## Final message
Counts (lines changed, keys created, seed rows, skipped, secrets), and every place left with a
company ID and why.
