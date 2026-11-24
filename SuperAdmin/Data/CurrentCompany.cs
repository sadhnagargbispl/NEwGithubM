namespace SuperAdmin.Data;

/// <summary>
/// Which company the Super Admin is editing right now. Taken from the URL
/// (/company/{id}/...) or, on other pages, from the last company opened.
/// Every settings section in the side menu points at this company.
/// </summary>
public static class CurrentCompany
{
    public const string Cookie = "sa.company";

    /// <summary>The settings sections, in side-menu order (route segment, menu name, icon, one plain line of what it is for).</summary>
    public static readonly (string Tab, string Name, string Icon, string Help)[] Sections =
    [
        ("details", "Company details", "M3 21h18M5 21V7l7-4 7 4v14M9 9h1M14 9h1M9 13h1M14 13h1M10 21v-4h4v4",
            "Name, address, GST, PAN, phone and email that the panels print on bills, invoices and mails."),
        ("websites", "Websites", "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18ZM3.6 9h16.8M3.6 15h16.8M12 3a15 15 0 0 1 0 18M12 3a15 15 0 0 0 0 18",
            "The website names this company's member and admin panels open on."),
        ("words", "Business Terms", "M4 7V4h16v3M9 20h6M12 4v16",
            "The business terms the panels use - Sponsor, Left / Right, Kit, TopUp, Wallet … Rename any of them for this company (e.g. “Sponsor” → “Introducer”). Leave a box empty to keep today's term."),
        ("units", "Point Names (PV / BV)", "M4 6h16M4 12h10M4 18h6",
            "What PV, BV, SP and other points are called on the panels, and whether they are shown."),
        ("forms", "Joining & Profile Forms","M9 11l3 3L22 4M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11",
            "Which boxes the joining and profile forms show, which ones must be filled, and what they are called."),
        ("branding", "Logo & Colours","M12 2a10 10 0 1 0 10 10c0-1.1-.9-2-2-2h-3a2 2 0 0 1-2-2V5c0-1.7-1.3-3-3-3Z",
            "Logo, colours, browser icon and footer of the panels. The picture on the right shows how it will look."),
        ("menus", "Menus", "M3 6h18M3 12h18M3 18h18",
            "The side menu of the member and admin panels: rename, re-order, show or hide items."),
        ("dashboard", "Member dashboard", "M3 3h7v9H3zM14 3h7v5h-7zM14 12h7v9h-7zM3 16h7v5H3z",
            "The boxes a member sees right after login: show or hide them, change their size and title."),
        ("panel", "Panel settings", "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z",
            "Switches and values for each panel page (what is shown, limits, texts). Search for what you want to change."),
        ("changes", "Change log", "M12 8v4l3 3M3.05 11a9 9 0 1 1 .5 4M3 4v7h7",
            "Everything that was changed for this company - when, what, and by whom."),
    ];

    /// <summary>Company id from the URL, else the remembered one, else null (none picked yet).</summary>
    public static int? Get(HttpContext ctx)
    {
        if (ctx.Request.RouteValues.TryGetValue("id", out var r) && ctx.Request.Path.StartsWithSegments("/company")
            && int.TryParse(r?.ToString(), out var fromUrl)) return fromUrl;
        return int.TryParse(ctx.Request.Cookies[Cookie], out var remembered) ? remembered : null;
    }
}
