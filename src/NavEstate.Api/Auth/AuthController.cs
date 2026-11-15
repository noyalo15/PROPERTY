using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NavEstate.Api.Common;
using NavEstate.Api.Data;
using NavEstate.Api.Domain;

namespace NavEstate.Api.Auth;

[ApiController, Route("api/auth")]
public class AuthController(
    SignInManager<AppUser> signIn, UserManager<AppUser> users, NavEstateDbContext db,
    ISmsSender sms, IOptions<AuthOptions> authOptions) : ControllerBase
{
    public record LoginRequest(string Email, string Password, bool RememberMe = false);
    public record TwoFactorRequest(string Code, bool RememberMe = false);
    public record OtpRequest(string Phone);
    public record OtpVerify(string Phone, string Code);
    public record CodeRequest(string Code);

    /// <summary>Staff and landlords: email + password, then TOTP when 2FA is on.</summary>
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var user = await users.FindByEmailAsync(req.Email.Trim());
        if (user is null || !user.Active) return Unauthorized(new { status = "invalid" });
        var r = await signIn.PasswordSignInAsync(user, req.Password, req.RememberMe, lockoutOnFailure: true);
        if (r.Succeeded) return Ok(new { status = "ok" });
        if (r.RequiresTwoFactor) return Ok(new { status = "requires_2fa" });
        if (r.IsLockedOut) return Unauthorized(new { status = "locked" });
        return Unauthorized(new { status = "invalid" });
    }

    [HttpPost("login-2fa"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> LoginTwoFactor(TwoFactorRequest req)
    {
        var code = req.Code.Replace(" ", "").Replace("-", "");
        var r = await signIn.TwoFactorAuthenticatorSignInAsync(code, req.RememberMe, rememberClient: false);
        if (r.Succeeded) return Ok(new { status = "ok" });
        if (r.IsLockedOut) return Unauthorized(new { status = "locked" });
        return Unauthorized(new { status = "invalid" });
    }

    /// <summary>Tenants: phone OTP by SMS. Always answers "sent" so it does not reveal which numbers exist.</summary>
    [HttpPost("otp/request"), AllowAnonymous, EnableRateLimiting("otp")]
    public async Task<IActionResult> RequestOtp(OtpRequest req)
    {
        var phone = Phone.Normalize(req.Phone);
        if (phone is null) return BadRequest(new { error = "invalid_phone" });
        var user = await users.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone && u.Active);
        if (user is not null && await users.IsInRoleAsync(user, Roles.Tenant) && !await users.IsLockedOutAsync(user))
        {
            var code = await users.GenerateChangePhoneNumberTokenAsync(user, phone);
            await sms.SendAsync(phone, $"Your NavEstate login code is {code}. It expires in a few minutes. Do not share it.");
        }
        return Ok(new { status = "sent" });
    }

    [HttpPost("otp/verify"), AllowAnonymous, EnableRateLimiting("otp")]
    public async Task<IActionResult> VerifyOtp(OtpVerify req)
    {
        var phone = Phone.Normalize(req.Phone);
        var user = phone is null ? null : await users.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone && u.Active);
        if (user is null || !await users.IsInRoleAsync(user, Roles.Tenant)) return Unauthorized(new { status = "invalid" });
        if (await users.IsLockedOutAsync(user)) return Unauthorized(new { status = "locked" });

        if (!await users.VerifyChangePhoneNumberTokenAsync(user, req.Code.Trim(), phone!))
        {
            await users.AccessFailedAsync(user);
            return Unauthorized(new { status = "invalid" });
        }
        await users.ResetAccessFailedCountAsync(user);
        if (!user.PhoneNumberConfirmed) { user.PhoneNumberConfirmed = true; await users.UpdateAsync(user); }
        await signIn.SignInAsync(user, isPersistent: false, authenticationMethod: "otp");
        return Ok(new { status = "ok" });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return Ok(new { status = "ok" });
    }

    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var roles = await users.GetRolesAsync(user);
        var isStaff = Roles.IsStaff(roles);
        var assignments = await db.UserPropertyAssignments.AsNoTracking().Where(a => a.UserId == user.Id)
            .Select(a => new { a.LandlordId, a.PropertyId }).ToListAsync();
        return Ok(new
        {
            user.Id, user.Email, user.FullName, user.PhoneNumber, roles, isStaff,
            twoFactorEnabled = user.TwoFactorEnabled,
            mustEnrolTwoFactor = isStaff && authOptions.Value.RequireStaffTwoFactor && !user.TwoFactorEnabled,
            user.TenantId, user.LandlordId, assignments
        });
    }

    /// <summary>Starts TOTP enrolment: returns the shared key and an otpauth:// URI for the QR code.</summary>
    [HttpPost("2fa/setup"), Authorize]
    public async Task<IActionResult> SetupTwoFactor()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (!user.TwoFactorEnabled) await users.ResetAuthenticatorKeyAsync(user);
        var key = await users.GetAuthenticatorKeyAsync(user);
        var issuer = UrlEncoder.Default.Encode("NavEstate");
        var uri = $"otpauth://totp/{issuer}:{UrlEncoder.Default.Encode(user.Email ?? user.UserName!)}?secret={key}&issuer={issuer}&digits=6";
        return Ok(new { sharedKey = key, otpauthUri = uri });
    }

    [HttpPost("2fa/enable"), Authorize]
    public async Task<IActionResult> EnableTwoFactor(CodeRequest req)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var code = req.Code.Replace(" ", "").Replace("-", "");
        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
            return BadRequest(new { error = "invalid_code" });
        await users.SetTwoFactorEnabledAsync(user, true);
        await signIn.RefreshSignInAsync(user);
        return Ok(new { status = "ok" });
    }

    public record ChangePassword(string CurrentPassword, string NewPassword);

    [HttpPost("password"), Authorize]
    public async Task<IActionResult> ChangeOwnPassword(ChangePassword req)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var r = await users.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!r.Succeeded) return BadRequest(new { errors = r.Errors.Select(e => e.Description) });
        await signIn.RefreshSignInAsync(user);
        return Ok(new { status = "ok" });
    }
}
