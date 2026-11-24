# Converting company (CompID) conditions to Super Admin settings

Goal: every `if CompID == "1109"`-style rule in the panels becomes a setting that
Super Admin can change per company — **without changing what any company sees
today** when nothing is set.

## The one rule

Wrap the ORIGINAL condition as the default. Never delete or rewrite it.

```csharp
// before
if (Session["CompID"].ToString() == "1109" || Session["CompID"].ToString() == "1113")
// after
if (PanelConfig.IsOn("fundwithdraw.kyc_required", Session["CompID"].ToString() == "1109" || Session["CompID"].ToString() == "1113"))
```

- Not set in Super Admin, or Super Admin unreachable → the original expression decides → exactly today.
- Super Admin "On"/"Off" for a company (or All companies) → overrides.

Helpers (App_Code/PanelConfig.cs):

| Need | Call | Default argument |
|---|---|---|
| yes/no | `PanelConfig.IsOn(key, <original bool expression>)` | the original condition |
| text shown / a string value | `PanelConfig.Text(key, <original string expression>)` | today's value |
| amount / percent / count | `PanelConfig.Number(key, <original decimal expression>)` | today's number |
| a word on screen | `PanelConfig.Label(key, "today's text")` | today's text |

`!=` conditions are wrapped the same way: `PanelConfig.IsOn("downline.leg_select", cid != "1106")`.

In .aspx markup inside `<% %>`: same call, e.g. `<% if (PanelConfig.IsOn("joining.show_pan", Session["CompID"].ToString() != "1113")) { %>`.

## VB.NET (Admin panel, Basic-MLM-ADMIN)

Helper: `App_Code/clsPanelConfig.vb` (class `PanelConfig`, same calls: `IsOn`, `Text`, `Number`, `Label`).
The Admin site compiles with VB 9 (.NET 3.5) — keep to simple expressions (no multi-line lambdas,
no string interpolation, no `?.`).

```vb
' before
If Session("CompID") = "1109" Or Session("CompID") = "1113" Then
' after
If PanelConfig.IsOn("fundwithdraw.kyc_required", Session("CompID") = "1109" Or Session("CompID") = "1113") Then
```

- `ElseIf <cond> Then` → `ElseIf PanelConfig.IsOn("key", <cond>) Then`.
- `IIf(<cond>, a, b)` → `IIf(PanelConfig.IsOn("key", <cond>), a, b)`.
- Markup: `<% If PanelConfig.IsOn("key", Session("CompID") = "1113") Then %>`.
- Text in markup: `<%= Server.HtmlEncode(PanelConfig.Label("key", "Today's Text")) %>`.
- **`Select Case Session("CompID")` blocks:** do NOT restructure them. Leave them as they are and list
  each one under `"skipped"` with reason `"select case - convert by hand"` and the line range.
- `'` comment lines and `<%-- --%>` blocks are not touched.
- Panel name in the catalog: `"panel": "admin"`.

## Common words and value names (Super Admin "Words" / "Value names" tabs)

These are NOT per-page keys - they are the shared terms every page uses. Only VISIBLE TEXT changes:

| Today's word on screen | Call (default = exactly the text that is there today) |
|---|---|
| Sponsor | `PanelConfig.Label("term.sponsor", "Sponsor")` |
| Sponsor ID | `PanelConfig.Label("term.sponsor_id", "Sponsor ID")` |
| Upliner / Upline | `PanelConfig.Label("term.upline", "Upliner")` (keep today's exact spelling as the default) |
| Left / Right (leg names) | `term.leg1` / `term.leg2` |
| Member, Distributor, Franchise, Shoppe, Kit, E-Pin, TopUp, Downline, Payout, Wallet | `term.member`, `term.distributor`, `term.franchise`, `term.shoppe`, `term.kit`, `term.epin`, `term.topup`, `term.downline`, `term.payout`, `term.wallet` |
| BV, PV, SP, EV, SV, RP (unit names) | `PanelConfig.ValueName("BV", "BV")` etc. |

Combined text: `Sponsor Name` → label(term.sponsor) + " Name"; `Left BV` → label(term.leg1) + " " + ValueName("BV").
The default MUST be the exact word as written on that page today (case and spelling), e.g. "SPONSOR ID" stays "SPONSOR ID".

ONLY these places:
- text between tags in .aspx/.ascx/.master markup (headings, labels, table headers, button text written as text);
- `Text="..."` / `HeaderText="..."` on `asp:Label`, `asp:Literal`, `asp:Button`, `asp:LinkButton`, TemplateField `HeaderTemplate` text — for `Text="..."`
  use `Text='<%# ... %>'` ONLY if the control is data-bound; otherwise set it in code-behind is NOT allowed — just skip it;
- code-behind lines that assign a literal to a UI property (`lblX.Text = "Sponsor ID"`, `HeaderRow.Cells(2).Text = "Left"`).

NEVER: anything inside `<script>` or `<style>`, CSS class names, `style=` (e.g. `text-align:left`), `align="left"`, ids, `value=` of
options/radios that the code reads, `DataField=`, SQL text, column names, dictionary keys, comparisons/conditions, URLs, file names,
JavaScript strings, BoundField `HeaderText` (no data binding there — skip), or text already wrapped in `PanelConfig.Label(...)`.
When unsure, skip. Markup output: C# `<%: PanelConfig.Label(...) %>`, VB `<%= Server.HtmlEncode(PanelConfig.Label(...)) %>`.
No catalog file is needed for words (the keys already exist in Super Admin); report counts per page instead.

## Keys

`<page>.<what_it_controls>` — lower case, `a-z 0-9 _ .`, max 100 chars, page = file name without
extension (e.g. `newjoiningfreeuc.pan_required`). One key per distinct behaviour: if the same
condition (same company list, same meaning) appears several times in one page, reuse one key.
If two places check the same companies but control different things, use two keys.

## Catalog (for Super Admin)

Every key you create must be listed in `SuperAdminConfig/catalog/<panel>-<group>.json`:

```json
{
  "settings": [
    {
      "key": "fundwithdraw.kyc_required",
      "panel": "member",
      "page": "Fundwithdraw.aspx",
      "kind": "switch",
      "label": "KYC must be approved before a withdrawal request",
      "today": "On for 1095, 1097, 1100, 1103 - off for others",
      "where": "Fundwithdraw.aspx.cs:212, 240"
    }
  ],
  "skipped": [
    { "where": "Fundwithdraw.aspx.cs:90", "reason": "picks the database connection" }
  ]
}
```

`kind` = `switch` | `text` | `number` | `label`. `label` = plain English a non-programmer
understands (what the member/admin sees change). `today` = who has it today.

## Do NOT

- change any SQL, any stored-procedure call, or anything not part of a CompID condition;
- touch commented-out code;
- convert conditions that pick a *data source* (connection, database name) or that are security
  checks (login, tokens, passwords) — list them in the catalog file under `"skipped"` with a reason;
- reformat files, change indentation elsewhere, or change line endings / encoding.
