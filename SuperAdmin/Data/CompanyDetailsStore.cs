using Dapper;
using Microsoft.Data.SqlClient;

namespace SuperAdmin.Data;

/// <summary>
/// The company's own details row - M_CompanyMaster in the COMPANY's database
/// (name, address, GST / PAN, contact, invoice text). Every panel reads this row
/// (invoices, bills, mails, page titles), so a save here shows everywhere at once.
/// The company database is found the way the panels find it: master
/// Proc_GetConnection1 @CompID, first row = the database they write to.
/// Columns differ between company databases, so only the ones that exist are shown;
/// password columns (SMS / mail / FTP) are never read or written here.
/// </summary>
public sealed class CompanyDetailsStore
{
    private readonly Db _db;
    public CompanyDetailsStore(Db db) => _db = db;

    public sealed record Field(string Column, string Label, string Group, string? Hint = null, string Kind = "text");

    /// <summary>The columns Super Admin may edit, in screen order.</summary>
    public static readonly Field[] Fields =
    [
        new("CompName", "Company name", "Name", "Full legal name - printed on invoices and bills"),
        new("CompTitle", "Short name / title", "Name", "Shown in page titles and mails"),
        new("CompAdd", "Address", "Address", "Printed on invoices, bills and letters", "multiline"),
        new("CompRegOffAdd", "Registered office address", "Address", null, "multiline"),
        new("CompCity", "City", "Address"),
        new("CompState", "State", "Address", "Decides GST type (CGST+SGST or IGST) on bills", "state"),
        new("CompGSTNo", "GST number", "Tax", "15 characters, e.g. 08ABCDE1234F1Z5", "gst"),
        new("CompCSTNo", "CST number (old)", "Tax", "Some older invoices print this box as the GST number", "gstish"),
        new("CompPANNo", "PAN", "Tax", "e.g. ABCDE1234F", "pan"),
        new("CompanyIDNo", "Company registration no. (CIN)", "Tax"),
        new("CompTinNo", "TIN (old)", "Tax"),
        new("CompSTaxNo", "Service tax no. (old)", "Tax"),
        new("MobileNo", "Mobile", "Contact", null, "phone"),
        new("ContactNo", "Phone / helpline", "Contact"),
        new("CompMail", "Email", "Contact", "Shown to members and used as the sender / reply address", "email"),
        new("WebSite", "Website", "Contact"),
        new("WebPortal", "Member portal address", "Contact"),
        new("MsgOnInvoice", "Message on invoice", "Invoice", null, "multiline"),
        new("CompTerm", "Terms on invoice", "Invoice", null, "multiline"),
    ];

    public sealed class Details
    {
        public bool Reachable { get; init; }
        public string? Problem { get; init; }
        public decimal? RowId { get; init; }                                   // CId of the row shown
        public Dictionary<string, string?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> MaxLength { get; } = new(StringComparer.OrdinalIgnoreCase); // -1 = max
        public List<(int Code, string Name)> States { get; } = [];
        public bool HasLastModified { get; init; }
        public int LastModifiedMax { get; init; }                               // -1 = max
        public string? LastModified { get; init; }
    }

    public async Task<Details> GetAsync(int companyId, CancellationToken ct = default)
    {
        string? cs;
        try { cs = await CompanyConnectionAsync(companyId, ct); }
        catch (Exception ex) { return new Details { Problem = "Could not ask the master database for this company's database: " + ex.Message }; }
        if (cs is null) return new Details { Problem = "The master database has no database connection for this company (Proc_GetConnection1 returned nothing)." };

        try
        {
            await using var c = new SqlConnection(cs);
            await c.OpenAsync(ct);
            var cols = (await c.QueryAsync<(string Name, int MaxLen)>("""
                SELECT name, CASE WHEN max_length = -1 THEN -1
                                  WHEN TYPE_NAME(system_type_id) IN ('nvarchar', 'nchar') THEN max_length / 2
                                  ELSE max_length END
                  FROM sys.columns WHERE object_id = OBJECT_ID('dbo.M_CompanyMaster');
                """)).ToDictionary(x => x.Name, x => x.MaxLen, StringComparer.OrdinalIgnoreCase);
            if (cols.Count == 0) return new Details { Problem = "This company's database has no M_CompanyMaster table." };

            var shown = Fields.Where(f => cols.ContainsKey(f.Column)).ToList();
            // Same row the panels use: they take the first row of the table.
            var select = "SELECT TOP 1 CId" + (cols.ContainsKey("LastModified") ? ", LastModified" : "")
                       + string.Concat(shown.Select(f => $", CAST([{f.Column}] AS nvarchar(max)) AS [{f.Column}]"))
                       + " FROM dbo.M_CompanyMaster ORDER BY CId;";
            var row = (IDictionary<string, object?>?)await c.QueryFirstOrDefaultAsync(select);
            if (row is null) return new Details { Problem = "This company's M_CompanyMaster table is empty - the company row has to be created in its database first." };

            var d = new Details
            {
                Reachable = true,
                RowId = Convert.ToDecimal(row["CId"]),
                HasLastModified = cols.ContainsKey("LastModified"),
                LastModifiedMax = cols.TryGetValue("LastModified", out var lmMax) ? lmMax : 0,
                LastModified = row.TryGetValue("LastModified", out var lm) ? lm?.ToString() : null,
            };
            foreach (var f in shown)
            {
                var v = row[f.Column]?.ToString()?.Trim();
                // State is a number column; "8.00" and "8" are the same state.
                if (f.Kind == "state" && decimal.TryParse(v, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n))
                    v = decimal.Truncate(n).ToString(System.Globalization.CultureInfo.InvariantCulture);
                d.Values[f.Column] = v;
                d.MaxLength[f.Column] = cols[f.Column];
            }
            if (shown.Any(f => f.Kind == "state"))
            {
                try
                {
                    d.States.AddRange(await c.QueryAsync<(int, string)>("""
                        SELECT CAST(StateCode AS int), LTRIM(RTRIM(StateName)) FROM dbo.M_StateDivMaster
                         WHERE ISNULL(ActiveStatus, 'Y') = 'Y' AND ISNULL(RowStatus, 'Y') = 'Y'
                         ORDER BY StateName;
                        """));
                }
                catch (SqlException) { /* no state list in this database - the code is typed in instead */ }
            }
            return d;
        }
        catch (Exception ex)
        {
            return new Details { Problem = "Could not open this company's database: " + ex.Message };
        }
    }

    /// <summary>Writes the changed values only. Returns (column, old, new) for each change.</summary>
    public async Task<List<(string Column, string? Old, string New)>> SaveAsync(int companyId, decimal rowId,
        IReadOnlyDictionary<string, string> values, string by, CancellationToken ct = default)
    {
        var cs = await CompanyConnectionAsync(companyId, ct) ?? throw new InvalidOperationException("No database for this company.");
        var current = await GetAsync(companyId, ct);
        if (!current.Reachable || current.RowId != rowId) throw new InvalidOperationException("The company row changed - open the page again.");

        var changes = new List<(string Column, string? Old, string New)>();
        foreach (var f in Fields)
        {
            if (!current.Values.ContainsKey(f.Column) || !values.TryGetValue(f.Column, out var v)) continue;
            if (!string.Equals(current.Values[f.Column] ?? "", v, StringComparison.Ordinal)) changes.Add((f.Column, current.Values[f.Column], v));
        }
        if (changes.Count == 0) return changes;

        // Column names come from the fixed list above, never from the request.
        var sets = string.Join(", ", changes.Select((ch, i) => $"[{ch.Column}] = @v{i}"));
        var p = new DynamicParameters();
        for (var i = 0; i < changes.Count; i++)
            p.Add($"v{i}", Fields.First(f => f.Column == changes[i].Column).Kind == "state" ? decimal.Parse(changes[i].New) : changes[i].New);
        p.Add("rowId", rowId);
        var stamp = $"Super Admin: {by} {DateTime.Now:dd-MMM-yyyy HH:mm}";
        if (current.LastModifiedMax > 0 && stamp.Length > current.LastModifiedMax) stamp = stamp[..current.LastModifiedMax];
        p.Add("stamp", stamp);

        await using (var c = new SqlConnection(cs))
        {
            await c.OpenAsync(ct);
            await c.ExecuteAsync($"UPDATE dbo.M_CompanyMaster SET {sets}{(current.HasLastModified ? ", LastModified = @stamp" : "")} WHERE CId = @rowId;", p);
        }

        // Change log lives in the master database.
        await using var m = await _db.OpenAsync(ct);
        await using var tx = m.BeginTransaction();
        foreach (var ch in changes)
            await ConfigStore.LogAsync(m, tx, companyId, "company", ch.Column, "update", ch.Old, ch.New, by);
        await tx.CommitAsync(ct);
        return changes;
    }

    /// <summary>The company database the panels write to (first row of Proc_GetConnection1).</summary>
    private async Task<string?> CompanyConnectionAsync(int companyId, CancellationToken ct)
    {
        await using var c = await _db.OpenAsync(ct);
        // The panels pass the id as text: Exec Proc_GetConnection1 '1113'
        var row = (IDictionary<string, object?>?)await c.QueryFirstOrDefaultAsync(
            "EXEC dbo.Proc_GetConnection1 @CompID;", new { CompID = companyId.ToString() });
        var raw = row is not null && row.TryGetValue("ConnectionString", out var v) ? v?.ToString() : null;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var b = new SqlConnectionStringBuilder(raw) { ApplicationName = "Super Admin" };
        if (!raw.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase)) b.TrustServerCertificate = true;
        // The legacy rows carry "Connect Timeout=200000000"; the panels' old client accepts it, but this
        // client turns it into milliseconds and overflows ("millisecondsDelay" out of range). Keep it sane.
        if (b.ConnectTimeout <= 0 || b.ConnectTimeout > 60) b.ConnectTimeout = 30;
        return b.ConnectionString;
    }
}
