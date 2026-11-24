using Dapper;
using Microsoft.AspNetCore.Identity;

namespace SuperAdmin.Data;

/// <summary>Super Admin users (SA_User) and their sign-in history (SA_LoginHistory).</summary>
public sealed class UserStore
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);

    private readonly Db _db;
    private readonly PasswordHasher<SaUser> _hasher = new();
    // A real hash of a random password, checked for unknown user names so they
    // take as long as a wrong password (no "this user exists" timing hint).
    private static readonly string DummyHash = new PasswordHasher<SaUser>().HashPassword(new SaUser(), Guid.NewGuid().ToString());

    public UserStore(Db db) => _db = db;

    public sealed class SaUser
    {
        public int UserID { get; init; }
        public string UserName { get; init; } = "";
        public string FullName { get; init; } = "";
        public string? Email { get; init; }
        public string? Mobile { get; init; }
        public string PasswordHash { get; init; } = "";
        public string Role { get; init; } = Roles.Admin;
        public bool IsActive { get; init; }
        public int FailedCount { get; init; }
        public DateTime? LockedUntil { get; init; }
        public DateTime? LastLoginAt { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    /// <summary>False when 03_superadmin_users.sql has not been run yet.</summary>
    public async Task<bool> TablesExistAsync(CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name IN ('SA_User', 'SA_LoginHistory');") == 2;
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.SA_User;");
    }

    /// <summary>What the sign-in cookie is checked against on every request.</summary>
    public async Task<(string UserName, string Role, bool IsActive)?> CurrentAsync(int userId, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        return await c.QueryFirstOrDefaultAsync<(string, string, bool)?>(
            "SELECT UserName, Role, IsActive FROM dbo.SA_User WHERE UserID = @userId;", new { userId });
    }

    public async Task<IReadOnlyList<SaUser>> ListAsync(CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        return (await c.QueryAsync<SaUser>("SELECT * FROM dbo.SA_User ORDER BY UserName;")).AsList();
    }

    public async Task<int> CreateAsync(string userName, string fullName, string? email, string? mobile,
                                       string role, string password, string createdBy, CancellationToken ct = default)
    {
        var hash = _hasher.HashPassword(new SaUser(), password);
        await using var c = await _db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>("""
            INSERT INTO dbo.SA_User (UserName, FullName, Email, Mobile, PasswordHash, Role, CreatedBy)
            VALUES (@userName, @fullName, @email, @mobile, @hash, @role, @createdBy);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { userName, fullName, email, mobile, hash, role, createdBy });
    }

    public enum SignInResult { Ok, Invalid, Locked, Inactive }

    /// <summary>
    /// Checks the password, counts failures and locks the account for
    /// <see cref="LockFor"/> after <see cref="MaxFailures"/> in a row. Every
    /// attempt is written to SA_LoginHistory.
    /// </summary>
    public async Task<(SignInResult Result, SaUser? User)> SignInAsync(string userName, string password,
        string? ip, string? userAgent, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        var u = await c.QueryFirstOrDefaultAsync<SaUser>(
            "SELECT * FROM dbo.SA_User WHERE UserName = @userName;", new { userName });

        SignInResult result;
        string reason;
        if (u is null)
        {
            _hasher.VerifyHashedPassword(new SaUser(), DummyHash, password);
            (result, reason) = (SignInResult.Invalid, "unknown_user");
        }
        else if (!u.IsActive) (result, reason) = (SignInResult.Inactive, "inactive");
        else if (u.LockedUntil is { } until && until > DateTime.Now) (result, reason) = (SignInResult.Locked, "locked");
        else if (_hasher.VerifyHashedPassword(u, u.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            (result, reason) = (SignInResult.Invalid, "bad_password");
            await c.ExecuteAsync("""
                UPDATE dbo.SA_User
                   SET FailedCount = FailedCount + 1,
                       LockedUntil = CASE WHEN FailedCount + 1 >= @max THEN DATEADD(MINUTE, @mins, GETDATE()) ELSE LockedUntil END
                 WHERE UserID = @id;
                """, new { id = u.UserID, max = MaxFailures, mins = (int)LockFor.TotalMinutes });
        }
        else
        {
            (result, reason) = (SignInResult.Ok, "ok");
            await c.ExecuteAsync(
                "UPDATE dbo.SA_User SET FailedCount = 0, LockedUntil = NULL, LastLoginAt = GETDATE() WHERE UserID = @id;",
                new { id = u.UserID });
        }

        await c.ExecuteAsync("""
            INSERT INTO dbo.SA_LoginHistory (UserName, Success, Reason, IpAddress, UserAgent)
            VALUES (@userName, @ok, @reason, @ip, @ua);
            """, new { userName = Trim(userName, 50), ok = result == SignInResult.Ok, reason, ip = Trim(ip, 50), ua = Trim(userAgent, 300) });

        return (result, result == SignInResult.Ok ? u : null);
    }

    private static string? Trim(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
