using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NavEstate.Api.Auth;
using NavEstate.Api.Bc;
using NavEstate.Api.Common;
using NavEstate.Api.Data;
using NavEstate.Api.Domain;

namespace NavEstate.Api.Features;

/// <summary>Setup → Users &amp; roles with property assignments.</summary>
[ApiController, Route("api/setup/users"), Authorize(Policy = Policies.UserAdmin)]
public class UsersController(UserManager<AppUser> users, NavEstateDbContext db, TimeProvider clock) : ControllerBase
{
    public record UserInput(string Email, string FullName, string? Phone, List<string> Roles, bool Active,
        List<Guid>? PropertyIds, List<Guid>? LandlordIds, Guid? TenantId, Guid? LandlordId, string? Password);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await db.Users.AsNoTracking().OrderBy(u => u.FullName).Select(u => new
        {
            u.Id, u.Email, u.FullName, Phone = u.PhoneNumber, u.Active, u.TwoFactorEnabled, u.TenantId, u.LandlordId,
            Roles = db.UserRoles.Where(r => r.UserId == u.Id).Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name).ToList(),
            PropertyIds = db.UserPropertyAssignments.Where(a => a.UserId == u.Id && a.PropertyId != null).Select(a => a.PropertyId).ToList(),
            LandlordIds = db.UserPropertyAssignments.Where(a => a.UserId == u.Id && a.LandlordId != null).Select(a => a.LandlordId).ToList(),
            LockedOut = u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow
        }).ToListAsync(ct);
        return Ok(list);
    }

    string? Validate(UserInput i) => Api.First(
        string.IsNullOrWhiteSpace(i.Email) || !i.Email.Contains('@') ? "A valid email is required" : null,
        string.IsNullOrWhiteSpace(i.FullName) ? "Name is required" : null,
        i.Roles.Count == 0 ? "Pick at least one role" : null,
        i.Roles.FirstOrDefault(r => !Roles.All.Contains(r)) is { } bad ? $"Unknown role {bad}" : null,
        i.Roles.Contains(Roles.Tenant) && i.Roles.Count > 1 ? "A tenant login cannot have staff roles" : null,
        i.Roles.Contains(Roles.Tenant) && (i.TenantId is null || Phone.Normalize(i.Phone) is null) ? "A tenant login needs the tenant record and a phone number" : null,
        i.Roles.Contains(Roles.Landlord) && i.LandlordId is null ? "A landlord login needs the landlord record" : null);

    [HttpPost]
    public async Task<IActionResult> Create(UserInput input, CancellationToken ct)
    {
        if (Validate(input) is { } err) return this.Error(err);
        var u = new AppUser
        {
            UserName = input.Email.Trim(), Email = input.Email.Trim(), EmailConfirmed = true, FullName = input.FullName.Trim(),
            PhoneNumber = Phone.Normalize(input.Phone), Active = input.Active, TenantId = input.TenantId, LandlordId = input.LandlordId,
            CreatedAt = clock.Now(), LockoutEnabled = true
        };
        var r = string.IsNullOrEmpty(input.Password) ? await users.CreateAsync(u) : await users.CreateAsync(u, input.Password);
        if (!r.Succeeded) return BadRequest(new { error = string.Join(" ", r.Errors.Select(e => e.Description)) });
        await users.AddToRolesAsync(u, input.Roles.Distinct());
        await SetAssignments(u.Id, input, ct);
        return Ok(new { u.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UserInput input, CancellationToken ct)
    {
        if (Validate(input) is { } err) return this.Error(err);
        var u = await users.FindByIdAsync(id);
        if (u is null) return NotFound();
        if (id == users.GetUserId(User) && (!input.Active || !input.Roles.Contains(Roles.SuperAdmin)))
            return this.Error("You cannot remove your own super admin role or deactivate yourself");
        u.Email = input.Email.Trim(); u.UserName = input.Email.Trim(); u.FullName = input.FullName.Trim();
        u.PhoneNumber = Phone.Normalize(input.Phone); u.Active = input.Active; u.TenantId = input.TenantId; u.LandlordId = input.LandlordId;
        var r = await users.UpdateAsync(u);
        if (!r.Succeeded) return BadRequest(new { error = string.Join(" ", r.Errors.Select(e => e.Description)) });
        var current = await users.GetRolesAsync(u);
        await users.RemoveFromRolesAsync(u, current.Except(input.Roles));
        await users.AddToRolesAsync(u, input.Roles.Except(current).Distinct());
        if (!input.Active) await users.UpdateSecurityStampAsync(u); // signs the user out everywhere
        if (!string.IsNullOrEmpty(input.Password))
        {
            var token = await users.GeneratePasswordResetTokenAsync(u);
            var pr = await users.ResetPasswordAsync(u, token, input.Password);
            if (!pr.Succeeded) return BadRequest(new { error = string.Join(" ", pr.Errors.Select(e => e.Description)) });
        }
        await SetAssignments(u.Id, input, ct);
        return Ok(new { u.Id });
    }

    /// <summary>Clears the user's authenticator so they enrol again (lost phone).</summary>
    [HttpPost("{id}/reset-2fa")]
    public async Task<IActionResult> ResetTwoFactor(string id)
    {
        var u = await users.FindByIdAsync(id);
        if (u is null) return NotFound();
        await users.SetTwoFactorEnabledAsync(u, false);
        await users.ResetAuthenticatorKeyAsync(u);
        await users.UpdateSecurityStampAsync(u);
        return Ok(new { status = "ok" });
    }

    async Task SetAssignments(string userId, UserInput input, CancellationToken ct)
    {
        var existing = await db.UserPropertyAssignments.Where(a => a.UserId == userId).ToListAsync(ct);
        db.UserPropertyAssignments.RemoveRange(existing);
        foreach (var p in input.PropertyIds?.Distinct() ?? []) db.UserPropertyAssignments.Add(new UserPropertyAssignment { UserId = userId, PropertyId = p });
        var landlords = (input.LandlordIds ?? []).ToList();
        if (input.LandlordId is { } own && input.Roles.Contains(Roles.Landlord)) landlords.Add(own);
        foreach (var l in landlords.Distinct()) db.UserPropertyAssignments.Add(new UserPropertyAssignment { UserId = userId, LandlordId = l });
        await db.SaveChangesAsync(ct);
    }
}

[ApiController, Route("api/bc"), Authorize(Policy = Policies.Staff)]
public class BcController(NavEstateDbContext db, Microsoft.Extensions.Options.IOptions<BcOptions> bcOptions) : ControllerBase
{
    /// <summary>Drives the top-bar pill: "BC 13 connected" or "BC offline" when the last Ping failed.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var last = await db.BcPingLog.AsNoTracking().OrderByDescending(p => p.At).FirstOrDefaultAsync(ct);
        return Ok(new { connected = last?.Ok == true, mock = bcOptions.Value.UseMock, lastPingAt = last?.At, message = last?.Message, durationMs = last?.DurationMs });
    }

    [HttpGet("queue"), Authorize(Policy = Policies.BcSync)]
    public async Task<IActionResult> Queue([FromQuery] string? status, [FromQuery] string? function, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        var q = db.BcSyncQueue.AsNoTracking();
        if (!string.IsNullOrEmpty(status)) q = q.Where(x => x.Status == status);
        if (!string.IsNullOrEmpty(function)) q = q.Where(x => x.FunctionName == function);
        var rows = await q.OrderByDescending(x => x.UpdatedAt).Take(Math.Clamp(take, 1, 1000)).Select(x => new
        {
            x.Id, x.EntityType, x.EntityId, x.FunctionName, x.ExternalRef, x.Status, x.Attempts, x.LastError,
            x.BcDocumentNo, x.NextAttemptAt, x.ProcessedAt, x.CreatedAt, x.UpdatedAt
        }).ToListAsync(ct);
        var today = DateTimeOffset.UtcNow.ToOffset(Nairobi.Offset).Date;
        var since = new DateTimeOffset(today, Nairobi.Offset);
        var kpi = new
        {
            postedToday = await db.BcSyncQueue.CountAsync(x => x.Status == "success" && x.ProcessedAt >= since, ct),
            firstTimeToday = await db.BcSyncQueue.CountAsync(x => x.Status == "success" && x.ProcessedAt >= since && x.Attempts == 1, ct),
            retrying = await db.BcSyncQueue.CountAsync(x => x.Status == "failed" && x.NextAttemptAt != null, ct),
            failed = await db.BcSyncQueue.CountAsync(x => x.Status == "failed" && x.NextAttemptAt == null, ct),
            pending = await db.BcSyncQueue.CountAsync(x => x.Status == "pending" || x.Status == "processing", ct)
        };
        return Ok(new { kpi, rows });
    }

    [HttpPost("queue/retry"), Authorize(Policy = Policies.BcSync)]
    public async Task<IActionResult> Retry([FromQuery] Guid? id, [FromServices] BcSyncWorker worker, CancellationToken ct) =>
        Ok(new { reset = await worker.RetryFailedAsync(id, ct) });

    /// <summary>Process the queue now (Hangfire also runs it every minute).</summary>
    [HttpPost("queue/run"), Authorize(Policy = Policies.BcSync)]
    public IActionResult RunNow([FromServices] IBackgroundJobClient jobs)
    {
        var id = jobs.Enqueue<BcSyncWorker>(w => w.ProcessDueAsync(CancellationToken.None));
        return Accepted(new { jobId = id });
    }

    [HttpPost("ping"), Authorize(Policy = Policies.BcSync)]
    public async Task<IActionResult> Ping([FromServices] BcHealthJobs jobs, CancellationToken ct) => Ok(await jobs.PingAsync(ct));
}

[ApiController, Route("api"), Authorize(Policy = Policies.Staff)]
public class SearchController(NavEstateDbContext db) : ControllerBase
{
    /// <summary>Top-bar global search: tenant (name, no., phone), unit code. Receipts join in stage 4.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return Ok(Array.Empty<object>());
        var s = q.Trim();
        var phone = Phone.Normalize(s);
        var tenants = await VisibleTenants().AsNoTracking()
            .Where(t => t.FullName.Contains(s) || t.TenantNo.Contains(s) || (phone != null && t.Phone == phone))
            .OrderBy(t => t.FullName).Take(8)
            .Select(t => new { kind = "tenant", id = t.Id, title = t.FullName, subtitle = t.TenantNo, link = "/tenants/" + t.Id }).ToListAsync(ct);
        var units = await db.Units.AsNoTracking().Where(u => u.Code.Contains(s)).OrderBy(u => u.Code).Take(8)
            .Select(u => new { kind = "unit", id = u.Id, title = u.Code, subtitle = u.Property!.Name, link = "/properties/" + u.PropertyId + "?unit=" + u.Id }).ToListAsync(ct);
        return Ok(tenants.Concat(units));
    }

    /// <summary>Leases and tenants follow the row-level security on units.</summary>
    IQueryable<Lease> VisibleActiveLeases() => db.Leases.Where(l => l.Status == "active" && db.Units.Any(u => u.Id == l.UnitId));
    IQueryable<Tenant> VisibleTenants() => User.IsInRole(Roles.SuperAdmin) ? db.Tenants
        : db.Tenants.Where(t => !t.Leases.Any() || t.Leases.Any(l => db.Units.Any(u => u.Id == l.UnitId)));

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var units = await db.Units.AsNoTracking().GroupBy(u => u.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int C(string s) => units.FirstOrDefault(u => u.Key == s)?.Count ?? 0;
        var total = units.Sum(u => u.Count);
        var props = await db.Properties.AsNoTracking().OrderBy(p => p.Name).Select(p => new
        {
            p.Id, p.Name, p.Code, Units = p.Units.Count, Occupied = p.Units.Count(u => u.Status == "occupied"),
            NotLettable = p.Units.Count(u => u.Status == "off_market" || u.Status == "maintenance")
        }).ToListAsync(ct);
        return Ok(new
        {
            units = total, occupied = C("occupied"), vacant = C("vacant"),
            occupancyPercent = PropertiesController.Occupancy(C("occupied"), total, C("off_market") + C("maintenance")),
            properties = props.Select(p => new { p.Id, p.Name, p.Code, p.Units, p.Occupied, OccupancyPercent = PropertiesController.Occupancy(p.Occupied, p.Units, p.NotLettable) }),
            tenants = await VisibleTenants().CountAsync(ct),
            activeLeases = await VisibleActiveLeases().CountAsync(ct),
            bcFailed = await db.BcSyncQueue.CountAsync(x => x.Status == "failed" && x.NextAttemptAt == null, ct),
            bcPending = await db.BcSyncQueue.CountAsync(x => x.Status == "pending" || x.Status == "processing" || (x.Status == "failed" && x.NextAttemptAt != null), ct),
            expiringIn90 = await VisibleActiveLeases().CountAsync(l => l.EndDate != null && l.EndDate <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90)), ct),
            escalationsDue = await VisibleActiveLeases().CountAsync(l => db.RentSchedules.Any(r => r.LeaseId == l.Id && r.Status == "pending_approval"), ct)
        });
    }
}
