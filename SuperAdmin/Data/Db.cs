using Microsoft.Data.SqlClient;

namespace SuperAdmin.Data;

/// <summary>
/// The legacy MASTER database (the one the panels' "sconstr" points to):
/// M_CompanyMasterNew, the Cfg_* settings tables and the SA_* user tables.
/// Connection string: ConnectionStrings:Master (user-secrets in development,
/// environment / appsettings.Production.json on the server - never in git).
/// </summary>
public sealed class Db
{
    private readonly string _cs;

    public Db(IConfiguration config)
    {
        var raw = config.GetConnectionString("Master");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("ConnectionStrings:Master is not set.");
        var b = new SqlConnectionStringBuilder(raw) { ApplicationName = "Super Admin" };
        // Internal server without a public certificate - trust it, as the panels do.
        if (!raw.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase)) b.TrustServerCertificate = true;
        _cs = b.ConnectionString;
        Server = b.DataSource;
    }

    /// <summary>The SQL server the master connection (web.config) points at.</summary>
    public string Server { get; }

    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var c = new SqlConnection(_cs);
        await c.OpenAsync(ct);
        return c;
    }
}

public static class Roles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Viewer = "viewer";
    public static readonly string[] All = [Owner, Admin, Viewer];
}

public static class Policies
{
    public const string CanEdit = "can-edit";
    public const string Owner = "owner";
}
