namespace SuperAdmin.Data;

/// <summary>
/// The settings Super Admin knows about, with what each panel does TODAY when
/// nothing is set. Add an entry here and it appears on the company screen;
/// the panels read it by its key (e.g. PanelConfig.Label("term.sponsor", "Sponsor")).
/// </summary>
public static class Catalog
{
    public sealed record Word(string Key, string Today, string? Note = null);
    public sealed record Unit(string Code, string Today);
    public enum RuleKind { Number, Switch, Text }
    public sealed record Rule(string Key, string Label, RuleKind Kind, string Today, string Group);

    /// <summary>The business words the panels show.</summary>
    public static readonly Word[] Words =
    [
        new("term.sponsor", "Sponsor", "Sponsor / Referral / Introducer"),
        new("term.sponsor_id", "Sponsor ID"),
        new("term.upline", "Upliner", "placement parent"),
        new("term.leg1", "Left", "Group A, ISP …"),
        new("term.leg2", "Right", "Group B, BSP …"),
        new("term.member", "Member"),
        new("term.distributor", "Distributor"),
        new("term.franchise", "Franchise"),
        new("term.shoppe", "Shoppe"),
        new("term.kit", "Kit", "package"),
        new("term.epin", "E-Pin"),
        new("term.topup", "TopUp", "Activation …"),
        new("term.downline", "Downline"),
        new("term.payout", "Payout"),
        new("term.wallet", "Wallet"),
    ];

    /// <summary>Value units (the code stays; only the shown name changes).</summary>
    public static readonly Unit[] Units =
    [
        new("BV", "BV"), new("PV", "PV"), new("SP", "SP"),
        new("EV", "EV"), new("SV", "SV"), new("RP", "RP"),
    ];

    // ---- forms ---------------------------------------------------------------

    /// <param name="CanHide">False for fields the form cannot work without (they can still be renamed).</param>
    public sealed record Field(string Key, string Today, string Group, bool CanHide = true, string? Note = null);
    public sealed record Form(string Key, string Name, string Where, Field[] Fields);

    private static readonly Field[] JoinFields =
    [
        new("sponsor_id", "Sponsor ID", "Sponsor & placement", CanHide: false),
        new("placement_id", "Upliner / placement ID", "Sponsor & placement"),
        new("leg", "Leg (Left / Right)", "Sponsor & placement"),
        new("user_name", "User name", "Sponsor & placement"),
        new("category", "Category", "Sponsor & placement"),
        new("title", "Title (Mr / Mrs / M/s)", "Personal"),
        new("first_name", "Name", "Personal", CanHide: false),
        new("father_name", "Father / husband name", "Personal"),
        new("dob", "Date of birth", "Personal"),
        new("marital_status", "Married?", "Personal"),
        new("anniversary", "Anniversary date", "Personal"),
        new("company_name", "Company name", "Personal", Note: "for business members"),
        new("registration_no", "Registration no.", "Personal", Note: "for business members"),
        new("address", "Address", "Address"),
        new("state", "State", "Address"),
        new("district", "District", "Address"),
        new("tehsil", "Tehsil / city", "Address"),
        new("village", "Village", "Address"),
        new("pincode", "Pincode", "Address"),
        new("postal_address", "Postal address (same as above)", "Address"),
        new("mobile", "Mobile no.", "Contact"),
        new("phone", "Phone (landline)", "Contact"),
        new("email", "E-mail", "Contact"),
        new("pan", "PAN no.", "KYC"),
        new("aadhaar", "Aadhaar no.", "KYC"),
        new("nominee", "Nominee name", "Nominee"),
        new("nominee_relation", "Relation with nominee", "Nominee"),
        new("account_no", "Bank account no.", "Bank"),
        new("account_type", "Account type", "Bank"),
        new("bank_name", "Bank name", "Bank"),
        new("branch", "Branch", "Bank"),
        new("ifsc", "IFSC code", "Bank"),
        new("micr", "MICR code", "Bank"),
        new("pay_mode", "Payment mode", "Payment"),
        new("dd_no", "DD / cheque no.", "Payment"),
        new("dd_date", "DD / cheque date", "Payment"),
        new("issue_bank", "Issuing bank", "Payment"),
        new("issue_branch", "Issuing branch", "Payment"),
        new("password", "Password", "Login", CanHide: false),
        new("transaction_password", "Transaction password", "Login"),
        new("terms", "Accept terms & conditions", "Login"),
    ];

    private static readonly string[] ProfileGroups = ["Personal", "Address", "Contact", "KYC", "Nominee", "Bank"];

    /// <summary>Forms whose fields Super Admin can show / hide / require / rename.</summary>
    public static readonly Form[] Forms =
    [
        new("register", "Joining (registration)", "Member cPanel and Admin joining pages", JoinFields),
        new("profile", "Member profile", "Member cPanel profile page", JoinFields.Where(f => ProfileGroups.Contains(f.Group)).ToArray()),
    ];

    // ---- member dashboard (cPanel Indextb.aspx) ------------------------------

    /// <param name="CanShow">False = its data is loaded only for some companies, so Super Admin may only hide it.</param>
    public sealed record DashBlock(string Key, string Today, string Note, bool CanShow = true, bool CanResize = true, bool HasTitle = true);

    /// <summary>Boxes on the member dashboard, top to bottom. Keys: dash.&lt;key&gt;.show / .width / .title</summary>
    public static readonly DashBlock[] Dashboard =
    [
        new("profile.card", "Profile card", "Photo, name, ID, joining date, rank · always shown today", HasTitle: false),
        new("referral.share_card", "My Referral Links", "Left/right links with share buttons · today only 1111-1114"),
        new("kpi.total_income", "Total Income", "Box · “Total Bonus” for 1111/1113/1114", CanResize: false),
        new("kpi.my_directs", "My Directs", "Box", CanResize: false),
        new("kpi.my_team", "My Team", "Box", CanResize: false),
        new("kpi.total_business", "Total Business", "Box · hidden for 1105/1106", CanResize: false),
        new("income.summary", "Income Summary", "Income by type · hidden for 1106/1113"),
        new("wallet.summary", "Wallet Summary", "Credit / debit / balance per wallet · hidden for 1106/1113"),
        new("withdrawal.status", "Withdrawal Status", "Approved / pending / rejected · today 1108/1111/1112/1114 (inside Wallet Summary)", CanResize: false),
        new("referral.left_right", "My Referral Link", "Left + right link rows · most companies"),
        new("referral.single", "My Referral Link (single)", "One link · today only 1109"),
        new("news.latest", "Latest Updates", "News from the admin"),
        new("team.summary", "My Team Summary", "Left / right / total counts · hidden for 1106"),
        new("directs.list", "My Direct Members", "Last 10 directs · hidden for 1106/1108"),
        new("referral.api_link", "Referral link (company API)", "Only 1106 · hide only", CanShow: false, HasTitle: false),
        new("kyc.status", "My KYC Status", "Only 1106 · hide only", CanShow: false, HasTitle: false),
        new("business.summary", "Business Summary", "Only 1113 · hide only", CanShow: false, HasTitle: false),
        new("package.kitwise", "Kit wise Summary", "Only 1108 · hide only", CanShow: false, HasTitle: false),
        new("volume.ppv_wbv", "Purchase volumes (PPV / WBV)", "Only 1106 · hide only", CanShow: false, CanResize: false, HasTitle: false),
    ];

    public static readonly (string Value, string Label)[] DashWidths =
        [("12", "Full width"), ("6", "Half"), ("4", "One third"), ("3", "One quarter")];

    /// <summary>Plain name for a change-log area.</summary>
    public static string AreaName(string area) => area switch
    {
        "label" => "Business term",
        "valuetype" => "Point name",
        "feature" => "Rule",
        "formfield" => "Joining / profile form",
        "branding" => "Logo & colours",
        "right" => "Page right",
        "menu" => "Menu",
        "website" => "Website",
        "company" => "Company",
        _ => area,
    };

    /// <summary>
    /// A change-log value as people read it. Stored forms: rule "value|enabled",
    /// value name "name|enabled|sort", word = the text itself.
    /// </summary>
    public static string? Readable(string area, string key, string? raw)
    {
        if (raw is null) return null;
        var parts = raw.Split('|');
        switch (area)
        {
            case "feature":
                var rule = Rules.FirstOrDefault(r => r.Key == key);
                if (rule?.Kind == RuleKind.Switch) return parts.Length > 1 ? (parts[1] == "1" ? "On" : "Off") : (parts[0] == "1" ? "On" : "Off");
                return parts[0];
            case "valuetype":
                return parts.Length > 1 && parts[1] == "0" ? $"{parts[0]} (hidden)" : parts[0];
            default:
                return raw;
        }
    }

    /// <summary>Plain name for a setting key, for the change log.</summary>
    public static string ItemName(string area, string key) => area switch
    {
        _ when key.StartsWith("dash.") => DashName(key),
        "label" => Words.FirstOrDefault(w => w.Key == key)?.Today ?? key,
        "feature" => Rules.FirstOrDefault(r => r.Key == key)?.Label ?? key,
        "formfield" => FieldName(key),
        _ => key,
    };

    // "dash.news.latest.width" -> "Dashboard: Latest Updates · width"
    private static string DashName(string key)
    {
        var rest = key["dash.".Length..];
        var dot = rest.LastIndexOf('.');
        if (dot < 0) return key;
        var block = Dashboard.FirstOrDefault(b => b.Key == rest[..dot]);
        return block is null ? key : $"Dashboard: {block.Today} · {rest[(dot + 1)..]}";
    }

    // Change-log key for a form field is "form/field".
    private static string FieldName(string key)
    {
        var parts = key.Split('/', 2);
        if (parts.Length != 2) return key;
        var form = Forms.FirstOrDefault(f => f.Key == parts[0]);
        var field = form?.Fields.FirstOrDefault(f => f.Key == parts[1]);
        return form is null || field is null ? key : $"{form.Name}: {field.Today}";
    }

    /// <summary>Rules that the panels today hard-code as "If CompID = …".</summary>
    public static readonly Rule[] Rules =
    [
        new("withdraw.min_amount", "Minimum withdrawal amount", RuleKind.Number, "500 (300 / 100 for some companies)", "Withdrawal"),
        new("withdraw.tds_percent", "TDS % on withdrawal", RuleKind.Number, "none (2% for company 1109)", "Withdrawal"),
        new("withdraw.kyc_required", "KYC required before withdrawal", RuleKind.Switch, "only some companies", "Withdrawal"),
        new("withdraw.one_per_day", "Only one withdrawal request per day", RuleKind.Switch, "only company 1060", "Withdrawal"),
        new("register.pan_required", "PAN required at registration", RuleKind.Switch, "required except 11 companies", "Registration"),
        new("register.pan_unique", "One PAN per member", RuleKind.Switch, "some companies", "Registration"),
        new("register.mobile_unique", "One mobile number per member", RuleKind.Switch, "companies 1107/1109/1113", "Registration"),
        new("register.email_unique", "One e-mail per member", RuleKind.Switch, "companies 1107/1109/1113", "Registration"),
        new("register.father_name_required", "Father name required", RuleKind.Switch, "required except 1111/1113", "Registration"),
        new("register.pincode_required", "Pincode required", RuleKind.Switch, "required except 1113", "Registration"),
        new("register.email_required", "E-mail required", RuleKind.Switch, "required except 1113", "Registration"),
        new("register.placement_optional", "Placement ID optional (defaults to sponsor)", RuleKind.Switch, "only company 1113", "Registration"),
        new("register.leg_select_visible", "Show the leg (Left/Right) choice", RuleKind.Switch, "shown except 1106", "Registration"),
        new("activation.min_amount", "Minimum activation amount", RuleKind.Number, "differs per company", "Activation"),
        new("login.otp_by_email", "Admin login needs e-mail OTP", RuleKind.Switch, "only company 1091", "Login"),
    ];
}
