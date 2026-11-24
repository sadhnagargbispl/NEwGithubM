using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using SuperAdmin.Data;

var builder = WebApplication.CreateBuilder(args);

// Every page needs a signed-in Super Admin unless it says [AllowAnonymous]
// (sign-in and first-time setup), and every form POST must carry the
// anti-forgery token.
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/account/login";
        o.LogoutPath = "/account/logout";
        o.AccessDeniedPath = "/account/denied";
        o.Cookie.Name = "sa.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        // Every request: the user must still exist, be switched on and have the same name and role.
        // A user switched off, removed or given another role is signed out at once (not after 8 hours).
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var p = ctx.Principal;
            if (!int.TryParse(p?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }
            var users = ctx.HttpContext.RequestServices.GetRequiredService<UserStore>();
            var now = await users.CurrentAsync(userId, ctx.HttpContext.RequestAborted);
            if (now is not { IsActive: true } u
                || !string.Equals(u.UserName, p!.FindFirstValue(ClaimTypes.Name), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(u.Role, p.FindFirstValue(ClaimTypes.Role), StringComparison.Ordinal))
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization(o =>
{
    // Owners and admins change settings; viewers only look.
    o.AddPolicy(Policies.CanEdit, p => p.RequireRole(Roles.Owner, Roles.Admin));
    o.AddPolicy(Policies.Owner, p => p.RequireRole(Roles.Owner));
});

builder.Services.AddSingleton<Db>();
builder.Services.AddScoped<UserStore>();
builder.Services.AddScoped<CompanyStore>();
builder.Services.AddScoped<ConfigStore>();
builder.Services.AddScoped<MenuStore>();
builder.Services.AddScoped<CompanyDetailsStore>();
builder.Services.AddScoped<NewCompanyStore>();
builder.Services.AddScoped<WebsiteStore>();
builder.Services.AddScoped<DatabaseNamesStore>();
builder.Services.AddSingleton<SettingsCatalog>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/home/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
