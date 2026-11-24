using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace SuperAdmin.Data;

/// <summary>
/// "Database names": when a company's databases are copied from another company
/// (e.g. pulstya, pulstyainv, pulstyaselect → harbalworld, harbalworldinv, harbalworldselect),
/// the stored procedures, views, functions and triggers still name the OLD databases
/// (pulstya..M_MemberMaster, pulstyainv.dbo.X). This finds every such reference and
/// rewrites it to the new name, keeping the suffix (inv / select / invselect ...).
/// Only database-name uses are touched: the name must be the first part of a three-part
/// name (pulstya..X, pulstya.dbo.X) and must not be preceded by an alias dot, @ or a word
/// character - so a column "a.pulstya", a table.column "pulstyaTemp.Col", a parameter
/// "@pulstya", text in quotes ('https://pulstya.com') and comments stay as they are.
/// Check = everything runs inside a transaction that is rolled back (nothing changes).
/// Apply = same, committed per database only when that database had no failure.
/// The login and password are used for this run only and are never stored or logged.
/// </summary>
public sealed partial class DatabaseNamesStore
{
    private readonly Db _db;
    public DatabaseNamesStore(Db db) => _db = db;

    public sealed record Request(string Server, string Login, string Password, IReadOnlyList<string> Databases, string OldName, string NewName);

    public sealed class DbResult
    {
        public required string Database { get; init; }
        public bool Opened { get; set; }
        public string? Error { get; set; }
        public int Objects { get; set; }            // objects whose text mentions the old name at all
        public int Changed { get; set; }            // objects rewritten
        public int References { get; set; }         // database-name references rewritten
        public int Encrypted { get; set; }          // objects whose text cannot be read
        public bool Committed { get; set; }
        public List<string> Failures { get; } = [];
        public List<string> Skipped { get; } = [];  // mention the old name only as column / parameter / comment
        public List<string> ChangedNames { get; } = [];
        public List<string> LeftInText { get; } = [];   // old name inside quotes / comments - left unchanged
    }

    public sealed class Result
    {
        public List<string> Errors { get; } = [];
        public List<DbResult> Databases { get; } = [];
        public bool Applied { get; set; }
        public int Changed => Databases.Sum(d => d.Changed);
        public int References => Databases.Sum(d => d.References);
        public int Failed => Databases.Sum(d => d.Failures.Count);
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{1,99}$")] private static partial Regex NameRx();
    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{1,128}$")] private static partial Regex DbNameRx();
    [GeneratedRegex(@"^[A-Za-z0-9.\-]{1,200}(\\[A-Za-z0-9_$\-]{1,50})?(,\d{1,5})?$")] private static partial Regex ServerRx();
    [GeneratedRegex(@"(?i)^CREATE\s+(PROCEDURE|PROC|VIEW|FUNCTION|TRIGGER)\b")] private static partial Regex CreateRx();

    public static IReadOnlyList<string> ParseDatabases(string? text) =>
        (text ?? "").Split([',', ';', '\n', '\r', ' ', '/', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public async Task<Result> RunAsync(Request r, bool apply, string by, CancellationToken ct = default)
    {
        var res = new Result();
        var oldName = (r.OldName ?? "").Trim();
        var newName = (r.NewName ?? "").Trim();
        if (!ServerRx().IsMatch(r.Server ?? "")) res.Errors.Add("Server: the server address, then a comma and the port (e.g. SERVER,1433).");
        if (string.IsNullOrWhiteSpace(r.Login)) res.Errors.Add("Give the SQL login of these databases.");
        if (string.IsNullOrEmpty(r.Password)) res.Errors.Add("Give the password of that login (it is used for this run only, never saved).");
        if (r.Databases.Count == 0) res.Errors.Add("List the databases to fix (e.g. harbalworld, harbalworldinv, harbalworldselect).");
        if (r.Databases.Count > 20) res.Errors.Add("At most 20 databases in one run.");
        foreach (var d in r.Databases.Where(d => !DbNameRx().IsMatch(d))) res.Errors.Add($"“{d}” is not a valid database name.");
        if (!NameRx().IsMatch(oldName)) res.Errors.Add("Old name: letters, digits and _ only (e.g. pulstya).");
        if (!NameRx().IsMatch(newName)) res.Errors.Add("New name: letters, digits and _ only (e.g. harbalworld).");
        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)) res.Errors.Add("Old and new name are the same.");
        if (res.Errors.Count > 0) return res;

        // pulstya, pulstyainv, [pulstyaselect] ... used as a DATABASE name, i.e. the first part of a
        // three-part name: db..X or db.schema.X. A two-part name (pulstyaTemp.Col = table.column,
        // alias.column) is never a database, so it is left alone. Text inside quotes and comments
        // is skipped separately (see Protected).
        var rx = new Regex(@"(?i)(?<![\w.@#$\]])(\[?)(" + Regex.Escape(oldName) + @")(\w*)(\]?)"
                         + @"(?=\s*\.\s*(?:\.|(?:\[[^\]\r\n]+\]|""[^""\r\n]+""|[A-Za-z_][\w$#@]*)\s*\.))");
        var anyRx = new Regex(@"(?i)(?<![\w@#$])" + Regex.Escape(oldName) + @"\w*\s*\.");

        foreach (var dbName in r.Databases)
        {
            var d = new DbResult { Database = dbName };
            res.Databases.Add(d);
            var cs = new SqlConnectionStringBuilder
            {
                DataSource = r.Server.Trim(), InitialCatalog = dbName, UserID = r.Login.Trim(), Password = r.Password,
                TrustServerCertificate = true, ConnectTimeout = 15, ApplicationName = "Super Admin - database names",
            }.ConnectionString;
            try
            {
                await using var c = new SqlConnection(cs);
                await c.OpenAsync(ct);
                d.Opened = true;
                d.Encrypted = await c.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sys.objects o WHERE o.type IN ('P','V','FN','IF','TF','TR') AND o.is_ms_shipped = 0 AND OBJECTPROPERTY(o.object_id, 'IsEncrypted') = 1;");
                var mods = (await c.QueryAsync<(string Schema, string Name, string Type, string Definition, bool Qi, bool AnsiNulls)>(
                    """
                    SELECT s.name, o.name, o.type_desc, m.definition, m.uses_quoted_identifier, m.uses_ansi_nulls
                      FROM sys.sql_modules m
                      JOIN sys.objects o ON o.object_id = m.object_id
                      JOIN sys.schemas s ON s.schema_id = o.schema_id
                     WHERE o.type IN ('P','V','FN','IF','TF','TR') AND o.is_ms_shipped = 0
                       AND m.definition LIKE '%' + @old + '%';
                    """, new { old = oldName })).ToList();
                d.Objects = mods.Count;

                await using var tx = c.BeginTransaction();
                foreach (var m in mods)
                {
                    var name = m.Schema + "." + m.Name;
                    var prot = Protected(m.Definition, m.Qi);
                    var hits = rx.Matches(m.Definition).Where(x => !InRanges(prot, x.Index)).ToList();
                    // old name written inside quotes / comments: never changed, but reported so it can be checked by eye
                    var inText = anyRx.Matches(m.Definition).Count(x => InRanges(prot, x.Index));
                    if (inText > 0) d.LeftInText.Add($"{name} ({inText})");
                    var n = hits.Count;
                    if (n == 0) { d.Skipped.Add(name); continue; }

                    var sb = new System.Text.StringBuilder(m.Definition.Length + n * 8);
                    var last = 0;
                    foreach (var mm in hits)
                    {
                        sb.Append(m.Definition, last, mm.Index - last)
                          .Append(mm.Groups[1].Value).Append(newName).Append(mm.Groups[3].Value).Append(mm.Groups[4].Value);
                        last = mm.Index + mm.Length;
                    }
                    sb.Append(m.Definition, last, m.Definition.Length - last);
                    var text = sb.ToString();
                    var what = string.Join(", ", hits
                        .GroupBy(x => (x.Groups[2].Value + x.Groups[3].Value).ToLowerInvariant())
                        .Select(g => $"{g.Key} → {newName}{g.First().Groups[3].Value} ×{g.Count()}"));
                    var at = LeadingSkip(text);
                    var head = CreateRx().Match(text.Substring(at));
                    if (!head.Success) { d.Failures.Add($"{name}: its text does not start with CREATE PROCEDURE / VIEW / FUNCTION / TRIGGER"); continue; }
                    text = text.Substring(0, at) + "ALTER " + head.Groups[1].Value + text.Substring(at + head.Length);
                    try
                    {
                        await c.ExecuteAsync($"SET QUOTED_IDENTIFIER {(m.Qi ? "ON" : "OFF")}; SET ANSI_NULLS {(m.AnsiNulls ? "ON" : "OFF")};", transaction: tx);
                        await c.ExecuteAsync(text, transaction: tx, commandTimeout: 120);
                        d.Changed++; d.References += n; d.ChangedNames.Add($"{name}: {what}");
                    }
                    catch (SqlException ex) { d.Failures.Add($"{name}: {ex.Message}"); }
                }
                if (apply && d.Failures.Count == 0 && d.Changed > 0) { await tx.CommitAsync(ct); d.Committed = true; }
                else await tx.RollbackAsync(ct);
            }
            catch (SqlException ex) { d.Error = ex.Message; }
        }

        if (apply && res.Databases.Any(x => x.Committed))
        {
            res.Applied = true;
            var summary = string.Join("; ", res.Databases.Where(x => x.Committed).Select(x => $"{x.Database}: {x.Changed} objects, {x.References} refs"));
            await using var m = await _db.OpenAsync(ct);
            await m.ExecuteAsync(
                "INSERT INTO dbo.Cfg_ChangeLog (CompanyID, Area, ItemKey, Action, OldValue, NewValue, ChangedBy) VALUES (0, 'database', @key, 'rename', @old, @summary, @by);",
                new { key = $"names:{oldName}->{newName}", old = oldName, summary = newName + " · " + summary, by });
        }
        return res;
    }

    /// <summary>
    /// Ranges [start, end) of the text that is not code: 'strings' (N'...' too, '' = a quote inside),
    /// -- line comments and /* block comments */ (they can nest in T-SQL). [names] and "names"
    /// are stepped over whole so a quote inside them is not taken as the start of a string.
    /// </summary>
    private static List<(int Start, int End)> Protected(string s, bool quotedIdentifier)
    {
        var r = new List<(int, int)>();
        var i = 0;
        while (i < s.Length)
        {
            var ch = s[i];
            if (ch == '[') { var j = s.IndexOf(']', i + 1); i = j < 0 ? s.Length : j + 1; continue; }
            if (ch == '"')
            {
                // QUOTED_IDENTIFIER ON: "x" is a name (code). OFF: "x" is a string - protect it.
                var j = s.IndexOf('"', i + 1);
                var end = j < 0 ? s.Length : j + 1;
                if (!quotedIdentifier) r.Add((i, end));
                i = end; continue;
            }
            if (ch == '\'')
            {
                var j = i + 1;
                while (j < s.Length)
                {
                    if (s[j] == '\'') { if (j + 1 < s.Length && s[j + 1] == '\'') { j += 2; continue; } break; }
                    j++;
                }
                var end = Math.Min(s.Length, j + 1);
                r.Add((i, end)); i = end; continue;
            }
            if (ch == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                var j = s.IndexOf('\n', i);
                var end = j < 0 ? s.Length : j;
                r.Add((i, end)); i = end; continue;
            }
            if (ch == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var depth = 1; var j = i + 2;
                while (j < s.Length && depth > 0)
                {
                    if (s[j] == '/' && j + 1 < s.Length && s[j + 1] == '*') { depth++; j += 2; }
                    else if (s[j] == '*' && j + 1 < s.Length && s[j + 1] == '/') { depth--; j += 2; }
                    else j++;
                }
                r.Add((i, j)); i = j; continue;
            }
            i++;
        }
        return r;
    }

    private static bool InRanges(List<(int Start, int End)> ranges, int at)
    {
        foreach (var (s, e) in ranges)
        {
            if (at < s) return false;   // ranges are in order
            if (at < e) return true;
        }
        return false;
    }

    // index of the first character that is not whitespace or a comment
    private static int LeadingSkip(string d)
    {
        var i = 0;
        while (i < d.Length)
        {
            if (char.IsWhiteSpace(d[i])) { i++; continue; }
            if (string.CompareOrdinal(d, i, "--", 0, 2) == 0) { var j = d.IndexOf('\n', i); if (j < 0) return d.Length; i = j + 1; continue; }
            if (string.CompareOrdinal(d, i, "/*", 0, 2) == 0) { var j = d.IndexOf("*/", i + 2, StringComparison.Ordinal); if (j < 0) return d.Length; i = j + 2; continue; }
            break;
        }
        return i;
    }
}
