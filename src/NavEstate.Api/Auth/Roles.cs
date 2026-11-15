using System.Security.Claims;

namespace NavEstate.Api.Auth;

public static class Roles
{
    public const string SuperAdmin = "super_admin";
    public const string Landlord = "landlord";
    public const string PropertyManager = "property_manager";
    public const string Accountant = "accountant";
    public const string Cashier = "cashier";
    public const string Caretaker = "caretaker";
    public const string Tenant = "tenant";

    public static readonly string[] All = [SuperAdmin, Landlord, PropertyManager, Accountant, Cashier, Caretaker, Tenant];
    public static readonly string[] Staff = [SuperAdmin, PropertyManager, Accountant, Cashier, Caretaker];

    public static bool IsStaff(IEnumerable<string> roles) => roles.Any(r => Staff.Contains(r));
}

public static class Policies
{
    public const string Staff = "staff";
    /// <summary>Setup pages: super_admin and accountant (spec: accountant has setup).</summary>
    public const string Setup = "setup";
    /// <summary>Create/edit properties, units, tenants: super_admin and property_manager.</summary>
    public const string Operations = "operations";
    public const string UserAdmin = "user_admin";
    /// <summary>BC Sync Log: super_admin and accountant.</summary>
    public const string BcSync = "bc_sync";
    public const string TenantPortal = "tenant_portal";
    public const string LandlordPortal = "landlord_portal";
}

public static class NavClaims
{
    public const string TwoFactorEnrolled = "navestate:2fa_enrolled";
}

public interface ICurrentUser
{
    string? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    /// <summary>True for super_admin and for background work (no HTTP request).</summary>
    bool BypassRowSecurity { get; }
}

public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    ClaimsPrincipal? P => accessor.HttpContext?.User;
    public string? UserId => P?.FindFirstValue(ClaimTypes.NameIdentifier);
    public bool IsAuthenticated => P?.Identity?.IsAuthenticated == true;
    public bool IsInRole(string role) => P?.IsInRole(role) == true;

    // No HTTP context = Hangfire job, seeder or migration: system access.
    public bool BypassRowSecurity => accessor.HttpContext is null || IsInRole(Roles.SuperAdmin);
}
