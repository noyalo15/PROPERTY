using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NavEstate.Api.Domain;

namespace NavEstate.Api.Auth;

public class AuthOptions
{
    public const string Section = "Auth";
    /// <summary>Spec: TOTP 2FA for staff roles. Staff without it can only reach /api/auth until they enrol.</summary>
    public bool RequireStaffTwoFactor { get; set; } = true;
}

/// <summary>Adds full name and a "2FA enrolled" claim to the auth cookie.</summary>
public class NavClaimsFactory(UserManager<AppUser> users, RoleManager<IdentityRole> roles, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(new Claim("name", user.FullName));
        id.AddClaim(new Claim(NavClaims.TwoFactorEnrolled, user.TwoFactorEnabled ? "true" : "false"));
        return id;
    }
}

public static class SecurityMiddleware
{
    /// <summary>
    /// 1. CSRF: unsafe /api requests must carry X-NavEstate: 1 (a cross-site form cannot set it; the cookie is also SameSite=Strict).
    /// 2. Staff must have TOTP 2FA before using anything except /api/auth.
    /// </summary>
    public static IApplicationBuilder UseNavEstateSecurity(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path;
        if (path.StartsWithSegments("/api"))
        {
            if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method)
                && !path.StartsWithSegments("/api/callbacks")
                && ctx.Request.Headers["X-NavEstate"] != "1")
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsJsonAsync(new { error = "missing_csrf_header" });
                return;
            }

            var u = ctx.User;
            var opts = ctx.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;
            if (opts.RequireStaffTwoFactor && u.Identity?.IsAuthenticated == true
                && Roles.Staff.Any(u.IsInRole)
                && u.FindFirstValue(NavClaims.TwoFactorEnrolled) != "true"
                && !path.StartsWithSegments("/api/auth"))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { error = "two_factor_enrolment_required" });
                return;
            }
        }
        await next();
    });
}
