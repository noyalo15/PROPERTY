using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NavEstate.Api.Auth;
using NavEstate.Api.Bc;
using NavEstate.Api.Common;
using NavEstate.Api.Data;
using NavEstate.Api.Domain;

namespace NavEstate.Api.Features;

/// <summary>Stage 2: lease wizard, activation, edits, renewal, termination and the rent schedule approval flow.</summary>
[ApiController, Route("api/leases"), Authorize(Policy = Policies.Staff)]
public class LeasesController(NavEstateDbContext db, NumberSeriesService numbers, BcSyncQueue bcQueue,
    RentScheduleService schedules, TimeProvider clock) : ControllerBase
{
    public record ChargeLine(string ChargeCode, string? Description, decimal Amount, string Frequency = "monthly", bool Active = true);

    public record LeaseInput(
        Guid TenantId, Guid UnitId, DateOnly StartDate, DateOnly? EndDate, string CurrencyCode, int BillingDay, string Frequency,
        List<ChargeLine> Charges, decimal DepositAmount, decimal WaterDepositAmount, int GraceDays, Guid? PenaltyRuleId,
        bool VatApplicable, bool WhtAgent, EscalationInput? Escalation, bool OverrideBlacklist = false);

    public record PreviewInput(decimal BaseRent, DateOnly StartDate, DateOnly? EndDate, EscalationInput? Escalation);
    public record ActivateInput(bool OverrideBlacklist = false);
    public record RenewInput(DateOnly NewEndDate);
    public record TerminateInput(DateOnly EndDate, string Reason);
    public record RentInput(decimal? MonthlyRent);

    /// <summary>Leases follow the units the user can see (units are filtered by row-level security).</summary>
    IQueryable<Lease> Visible() => db.Leases.Where(l => db.Units.Any(u => u.Id == l.UnitId));

    DateOnly Today => clock.Today();

    // ---------------------------------------------------------------- list

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] string? status, [FromQuery] Guid? propertyId,
        [FromQuery] string? currency, [FromQuery] int? expiring, [FromQuery] bool? escalationsDue,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        var today = Today;
        var query = Visible().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(l => l.LeaseNo.Contains(s) || l.Tenant!.FullName.Contains(s) || l.Tenant.TenantNo.Contains(s) || l.Unit!.Code.Contains(s));
        }
        if (!string.IsNullOrEmpty(status)) query = query.Where(l => l.Status == status);
        if (propertyId is not null) query = query.Where(l => l.Unit!.PropertyId == propertyId);
        if (!string.IsNullOrEmpty(currency)) query = query.Where(l => l.CurrencyCode == currency);
        if (expiring is > 0)
        {
            var limit = today.AddDays(expiring.Value);
            query = query.Where(l => l.Status == "active" && l.EndDate != null && l.EndDate >= today && l.EndDate <= limit);
        }
        if (escalationsDue == true) query = query.Where(l => l.Status == "active" && db.RentSchedules.Any(r => r.LeaseId == l.Id && r.Status == "pending_approval"));

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(l => l.Status == "active" ? 0 : l.Status == "draft" ? 1 : 2).ThenBy(l => l.EndDate ?? DateOnly.MaxValue).ThenBy(l => l.LeaseNo)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(l => new
            {
                l.Id, l.LeaseNo, l.Status, l.StartDate, l.EndDate, l.CurrencyCode, l.Frequency,
                l.TenantId, Tenant = l.Tenant!.FullName, l.Tenant.TenantNo, l.UnitId, Unit = l.Unit!.Code, Property = l.Unit.Property!.Name,
                MonthlyRent = db.RentSchedules.Where(r => r.LeaseId == l.Id && r.Status == "current").Select(r => r.MonthlyRent).FirstOrDefault()
                    ?? l.Charges.Where(c => c.Active && db.ChargeCodes.Any(cc => cc.Code == c.ChargeCode && cc.IsRent)).Sum(c => c.Amount),
                NextChange = db.RentSchedules.Where(r => r.LeaseId == l.Id && (r.Status == "scheduled" || r.Status == "pending_approval" || r.Status == "approved"))
                    .OrderBy(r => r.EffectiveFrom).Select(r => new { r.EffectiveFrom, r.MonthlyRent, r.Status }).FirstOrDefault(),
                NoticeDue = db.RentSchedules.Any(r => r.LeaseId == l.Id && r.Status == "pending_approval")
            }).ToListAsync(ct);
        return Ok(new { total, page, pageSize, rows });
    }

    /// <summary>Counts for the filter chips on the leases list.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var today = Today;
        var active = Visible().AsNoTracking().Where(l => l.Status == "active");
        return Ok(new
        {
            active = await active.CountAsync(ct),
            drafts = await Visible().CountAsync(l => l.Status == "draft", ct),
            expiring30 = await active.CountAsync(l => l.EndDate != null && l.EndDate >= today && l.EndDate <= today.AddDays(30), ct),
            expiring60 = await active.CountAsync(l => l.EndDate != null && l.EndDate >= today && l.EndDate <= today.AddDays(60), ct),
            expiring90 = await active.CountAsync(l => l.EndDate != null && l.EndDate >= today && l.EndDate <= today.AddDays(90), ct),
            escalationsDue = await active.CountAsync(l => db.RentSchedules.Any(r => r.LeaseId == l.Id && r.Status == "pending_approval"), ct)
        });
    }

    // ---------------------------------------------------------------- wizard helpers

    /// <summary>Wizard step 2: vacant (or reserved) units the user can see, with their default charges.</summary>
    [HttpGet("vacant-units")]
    public async Task<IActionResult> VacantUnits([FromQuery] string? q, [FromQuery] Guid? propertyId, CancellationToken ct)
    {
        var query = db.Units.AsNoTracking().Where(u => u.Status == "vacant" || u.Status == "reserved")
            .Where(u => !db.Leases.Any(l => l.UnitId == u.Id && l.Status == "active"));
        if (propertyId is not null) query = query.Where(u => u.PropertyId == propertyId);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(u => u.Code.Contains(q.Trim()) || u.Property!.Name.Contains(q.Trim()));
        var rows = await query.OrderBy(u => u.Property!.Name).ThenBy(u => u.Code).Take(100).Select(u => new
        {
            u.Id, u.Code, u.UnitType, u.Status, u.DefaultRent, u.DefaultCurrency, u.PropertyId, Property = u.Property!.Name, PropertyType = u.Property.Type,
            Charges = u.Charges.OrderBy(c => c.ChargeCode).Select(c => new { c.ChargeCode, c.Amount, c.CurrencyCode, c.Frequency })
        }).ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>Wizard escalation and review steps: the rows the rule will generate.</summary>
    [HttpPost("schedule-preview")]
    public IActionResult Preview(PreviewInput i)
    {
        if (i.BaseRent <= 0) return this.Error("Enter the monthly rent first");
        if (i.EndDate is not null && i.EndDate <= i.StartDate) return this.Error("End date must be after the start date");
        if (RentScheduleGenerator.Validate(i.Escalation, i.StartDate, i.EndDate) is { } err) return this.Error(err);
        return Ok(RentScheduleGenerator.Generate(i.BaseRent, i.StartDate, i.EndDate, i.Escalation));
    }

    // ---------------------------------------------------------------- detail

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var l = await Visible().AsNoTracking().Include(x => x.Charges).Include(x => x.Tenant).Include(x => x.Unit).ThenInclude(u => u!.Property)
            .Include(x => x.PenaltyRule).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound();
        var codes = await db.ChargeCodes.AsNoTracking().ToDictionaryAsync(c => c.Code, ct);
        var rule = await db.RentEscalationRules.AsNoTracking().FirstOrDefaultAsync(r => r.LeaseId == id, ct);
        var rows = await db.RentSchedules.AsNoTracking().Where(r => r.LeaseId == id).OrderBy(r => r.EffectiveFrom).ToListAsync(ct);
        var approverIds = rows.Where(r => r.ApprovedBy is not null).Select(r => r.ApprovedBy!).Distinct().ToList();
        var approvers = await db.Users.AsNoTracking().Where(u => approverIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var deposits = await db.Deposits.AsNoTracking().Where(d => d.LeaseId == id)
            .Select(d => new { d.Id, d.DepositType, d.CurrencyCode, d.AmountHeld, d.Status }).ToListAsync(ct);
        var current = rows.FirstOrDefault(r => r.Status == "current");
        var rentCharge = l.Charges.Where(c => c.Active && codes.TryGetValue(c.ChargeCode, out var cc) && cc.IsRent).Sum(c => c.Amount);
        var sync = await db.BcSyncQueue.AsNoTracking().Where(x => x.EntityType == "tenant" && x.EntityId == l.TenantId)
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Status, x.ProcessedAt }).FirstOrDefaultAsync(ct);

        return Ok(new
        {
            lease = new
            {
                l.Id, l.LeaseNo, l.Status, l.StartDate, l.EndDate, l.CurrencyCode, l.BillingDay, l.Frequency, l.DepositAmount, l.WaterDepositAmount,
                l.GraceDays, l.PenaltyRuleId, PenaltyRule = l.PenaltyRule?.Name, l.VatApplicable, l.WhtAgent, l.EndReason, l.EndedAt, l.CreatedAt,
                MonthlyRent = current?.MonthlyRent ?? rentCharge
            },
            tenant = new { l.Tenant!.Id, l.Tenant.TenantNo, l.Tenant.FullName, l.Tenant.Phone, l.Tenant.Email, l.Tenant.Blacklisted, l.Tenant.BcSyncedAt, BcStatus = sync?.Status },
            unit = new { l.Unit!.Id, l.Unit.Code, l.Unit.UnitType, l.Unit.Status, PropertyId = l.Unit.PropertyId, Property = l.Unit.Property!.Name, PropertyCode = l.Unit.Property.Code },
            charges = l.Charges.OrderByDescending(c => codes.TryGetValue(c.ChargeCode, out var cc) && cc.IsRent).ThenBy(c => c.ChargeCode).Select(c => new
            {
                c.Id, c.ChargeCode, c.Description, c.Amount, c.CurrencyCode, c.Frequency, c.Active,
                IsRent = codes.TryGetValue(c.ChargeCode, out var cc) && cc.IsRent,
                BcAccount = codes.TryGetValue(c.ChargeCode, out var cd) ? cd.Description : null
            }),
            escalation = rule is null ? null : new { rule.Type, rule.Value, rule.EveryMonths, rule.Compounding, rule.NoticeDays, rule.RequiresApproval },
            schedule = rows.Select(r => new
            {
                r.Id, r.EffectiveFrom, r.EffectiveTo, r.MonthlyRent, r.ChangeLabel, r.Status, r.NoticeSentAt,
                ApprovedBy = r.ApprovedBy is null ? null : r.ApprovedBy == RentScheduleService.SystemUser ? "Automatic" : approvers.GetValueOrDefault(r.ApprovedBy, r.ApprovedBy),
                NoticeDate = r.EffectiveFrom == l.StartDate ? (DateOnly?)null : RentScheduleGenerator.NoticeDate(r, rule),
                Overdue = r.Status is "pending_approval" or "scheduled" && r.EffectiveFrom <= Today
            }),
            deposits,
            notices = await db.Notices.AsNoTracking().Where(n => n.TenantId == l.TenantId && n.Subject != null && n.Subject.EndsWith(l.LeaseNo))
                .OrderByDescending(n => n.CreatedAt).Take(20).Select(n => new { n.Id, n.Channel, n.Subject, n.Body, n.Status, n.SentAt, n.CreatedAt }).ToListAsync(ct)
        });
    }

    // ---------------------------------------------------------------- create / edit

    async Task<string?> ValidateAsync(LeaseInput i, Lease? existing, HashSet<string> rentCodes, CancellationToken ct)
    {
        var err = Api.First(
            Api.In(i.CurrencyCode, V.Currency, "currency"), Api.In(i.Frequency, V.Frequency, "frequency"),
            i.BillingDay is < 1 or > 28 ? "Billing day must be between 1 and 28" : null,
            i.EndDate is not null && i.EndDate <= i.StartDate ? "End date must be after the start date" : null,
            i.DepositAmount < 0 || i.WaterDepositAmount < 0 ? "Deposits cannot be negative" : null,
            i.GraceDays is < 0 or > 60 ? "Grace days must be between 0 and 60" : null,
            i.Charges is null || i.Charges.Count == 0 ? "Add the lease charges (at least the rent)" : null);
        if (err is not null) return err;
        var known = await db.ChargeCodes.AsNoTracking().Where(c => c.Active).Select(c => c.Code).ToListAsync(ct);
        foreach (var c in i.Charges)
        {
            err = Api.First(
                known.Contains(c.ChargeCode) ? null : $"Unknown or inactive charge code {c.ChargeCode}",
                Api.In(c.Frequency, V.ChargeFrequency, "charge frequency"),
                c.Amount < 0 ? $"{c.ChargeCode} amount cannot be negative" : null);
            if (err is not null) return err;
        }
        var rentLines = i.Charges.Where(c => c.Active && rentCodes.Contains(c.ChargeCode)).ToList();
        if (rentLines.Count != 1) return "The lease needs exactly one active rent charge (RENT or RENTCOM)";
        if (rentLines[0].Amount <= 0) return "Monthly rent must be more than 0";
        if (i.Charges.GroupBy(c => c.ChargeCode).Any(g => g.Count() > 1)) return "Each charge code can appear only once";
        if (i.PenaltyRuleId is not null && !await db.PenaltyRules.AnyAsync(p => p.Id == i.PenaltyRuleId, ct)) return "Penalty rule not found";
        if (RentScheduleGenerator.Validate(i.Escalation, i.StartDate, i.EndDate) is { } e) return e;

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == i.TenantId, ct);
        if (tenant is null) return "Tenant not found";
        if (tenant.Blacklisted && !i.OverrideBlacklist)
            return $"{tenant.FullName} is blacklisted ({tenant.BlacklistReason}). Tick the override to let to them anyway.";
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == i.UnitId, ct);
        if (unit is null) return "Unit not found";
        if (existing?.UnitId != i.UnitId && unit.Status is not ("vacant" or "reserved"))
            return $"Unit {unit.Code} is {unit.Status.Replace('_', ' ')}; only vacant units can be let";
        return null;
    }

    void Apply(Lease l, LeaseInput i)
    {
        l.TenantId = i.TenantId; l.UnitId = i.UnitId; l.StartDate = i.StartDate; l.EndDate = i.EndDate; l.CurrencyCode = i.CurrencyCode;
        l.BillingDay = i.BillingDay; l.Frequency = i.Frequency; l.DepositAmount = i.DepositAmount; l.WaterDepositAmount = i.WaterDepositAmount;
        l.GraceDays = i.GraceDays; l.PenaltyRuleId = i.PenaltyRuleId; l.VatApplicable = i.VatApplicable; l.WhtAgent = i.WhtAgent;
        SetCharges(l, i.Charges, i.CurrencyCode);
    }

    void SetCharges(Lease l, List<ChargeLine> lines, string currency)
    {
        foreach (var gone in l.Charges.Where(c => lines.All(x => x.ChargeCode != c.ChargeCode)).ToList())
        {
            l.Charges.Remove(gone);
            db.LeaseCharges.Remove(gone);
        }
        foreach (var x in lines)
        {
            var c = l.Charges.FirstOrDefault(c => c.ChargeCode == x.ChargeCode);
            if (c is null) l.Charges.Add(c = new LeaseCharge { ChargeCode = x.ChargeCode });
            c.Description = string.IsNullOrWhiteSpace(x.Description) ? x.ChargeCode : x.Description.Trim()[..Math.Min(50, x.Description.Trim().Length)];
            c.Amount = x.Amount; c.CurrencyCode = currency; c.Frequency = x.Frequency; c.Active = x.Active;
        }
    }

    /// <summary>Replaces the escalation rule and the schedule of a draft lease.</summary>
    async Task RebuildScheduleAsync(Lease l, EscalationInput? rule, decimal baseRent, CancellationToken ct)
    {
        db.RentEscalationRules.RemoveRange(await db.RentEscalationRules.Where(r => r.LeaseId == l.Id).ToListAsync(ct));
        db.RentSchedules.RemoveRange(await db.RentSchedules.Where(r => r.LeaseId == l.Id).ToListAsync(ct));
        if (rule is not null)
            db.RentEscalationRules.Add(new RentEscalationRule
            {
                LeaseId = l.Id, Type = rule.Type, Value = rule.Type is "stepped" or "market_review" ? 0 : rule.Value,
                EveryMonths = rule.Type == "stepped" ? 12 : rule.EveryMonths, Compounding = rule.Compounding,
                NoticeDays = rule.NoticeDays, RequiresApproval = rule.RequiresApproval
            });
        foreach (var r in RentScheduleGenerator.Generate(baseRent, l.StartDate, l.EndDate, rule))
            db.RentSchedules.Add(new RentSchedule { LeaseId = l.Id, EffectiveFrom = r.From, EffectiveTo = r.To, MonthlyRent = r.MonthlyRent, ChangeLabel = r.Label, Status = "scheduled" });
    }

    /// <summary>Saves the wizard as a draft lease (LSE-nnnn); with ?activate=true it is activated in the same transaction.</summary>
    [HttpPost, Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> Create(LeaseInput input, [FromQuery] bool activate, CancellationToken ct)
    {
        var rentCodes = await schedules.RentCodesAsync(ct);
        if (await ValidateAsync(input, null, rentCodes, ct) is { } err) return this.Error(err);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var l = new Lease { LeaseNo = await numbers.NextLeaseNoAsync(ct), Status = "draft" };
        Apply(l, input);
        db.Leases.Add(l);
        await RebuildScheduleAsync(l, input.Escalation, input.Charges.First(c => c.Active && rentCodes.Contains(c.ChargeCode)).Amount, ct);
        if (await this.TrySaveAsync(db, ct) is { } fail) return fail;
        if (activate && await ActivateCoreAsync(l, input.OverrideBlacklist, ct) is { } bad) return bad;
        await tx.CommitAsync(ct);
        return Ok(new { l.Id, l.LeaseNo, l.Status });
    }

    /// <summary>
    /// Drafts can change everything. Active leases keep tenant, unit, dates, currency, rent and escalation
    /// (rent changes go through the schedule, the end date through renewal); other terms and charges can change.
    /// </summary>
    [HttpPut("{id:guid}"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> Update(Guid id, LeaseInput input, CancellationToken ct)
    {
        var l = await Visible().Include(x => x.Charges).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound();
        var rentCodes = await schedules.RentCodesAsync(ct);
        if (l.Status == "draft")
        {
            if (await ValidateAsync(input, l, rentCodes, ct) is { } err) return this.Error(err);
            Apply(l, input);
            await RebuildScheduleAsync(l, input.Escalation, input.Charges.First(c => c.Active && rentCodes.Contains(c.ChargeCode)).Amount, ct);
            return await this.TrySaveAsync(db, ct) ?? Ok(new { l.Id, l.LeaseNo, l.Status });
        }
        if (l.Status != "active") return this.Error($"A {l.Status} lease cannot be edited");

        var rent = l.Charges.FirstOrDefault(c => c.Active && rentCodes.Contains(c.ChargeCode));
        var newRent = input.Charges?.FirstOrDefault(c => c.Active && rentCodes.Contains(c.ChargeCode));
        var locked = Api.First(
            input.TenantId != l.TenantId || input.UnitId != l.UnitId ? "Tenant and unit cannot change on an active lease (use Transfer unit)" : null,
            input.StartDate != l.StartDate || input.EndDate != l.EndDate ? "Dates cannot change on an active lease (use Renew or Terminate)" : null,
            input.CurrencyCode != l.CurrencyCode ? "Currency cannot change on an active lease" : null,
            newRent is null || rent is null || newRent.ChargeCode != rent.ChargeCode || newRent.Amount != rent.Amount
                ? "Rent changes go through the rent schedule (escalation), not the charges" : null,
            input.Escalation is not null ? "The escalation rule cannot change on an active lease" : null,
            Api.In(input.Frequency, V.Frequency, "frequency"),
            input.BillingDay is < 1 or > 28 ? "Billing day must be between 1 and 28" : null,
            input.GraceDays is < 0 or > 60 ? "Grace days must be between 0 and 60" : null);
        if (locked is not null) return this.Error(locked);
        var known = await db.ChargeCodes.AsNoTracking().Where(c => c.Active).Select(c => c.Code).ToListAsync(ct);
        if (input.Charges!.FirstOrDefault(c => !known.Contains(c.ChargeCode) || Api.In(c.Frequency, V.ChargeFrequency, "f") is not null || c.Amount < 0) is { } bad)
            return this.Error($"Charge {bad.ChargeCode} is not valid");
        if (input.Charges!.GroupBy(c => c.ChargeCode).Any(g => g.Count() > 1)) return this.Error("Each charge code can appear only once");
        if (input.PenaltyRuleId is not null && !await db.PenaltyRules.AnyAsync(p => p.Id == input.PenaltyRuleId, ct)) return this.Error("Penalty rule not found");

        l.BillingDay = input.BillingDay; l.Frequency = input.Frequency; l.GraceDays = input.GraceDays; l.PenaltyRuleId = input.PenaltyRuleId;
        l.VatApplicable = input.VatApplicable; l.WhtAgent = input.WhtAgent; l.DepositAmount = input.DepositAmount; l.WaterDepositAmount = input.WaterDepositAmount;
        SetCharges(l, input.Charges!, l.CurrencyCode);
        return await this.TrySaveAsync(db, ct) ?? Ok(new { l.Id, l.LeaseNo, l.Status });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var l = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound();
        if (l.Status != "draft") return this.Error("Only draft leases can be deleted");
        db.Leases.Remove(l);
        return await this.TrySaveAsync(db, ct) ?? NoContent();
    }

    // ---------------------------------------------------------------- activation

    [HttpPost("{id:guid}/activate"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> Activate(Guid id, ActivateInput? input, CancellationToken ct)
    {
        var l = await Visible().Include(x => x.Charges).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await ActivateCoreAsync(l, input?.OverrideBlacklist ?? false, ct) is { } bad) return bad;
        await tx.CommitAsync(ct);
        return Ok(new { l.Id, l.LeaseNo, l.Status });
    }

    /// <summary>
    /// Draft → active: the unit becomes occupied, deposit records are created (held, awaiting receipt),
    /// the schedule's past rows are settled, notices already due are flagged and UpsertTenant is queued
    /// again because the BC PROPERTY and UNIT dimensions change. The filtered unique index on
    /// leases(unit_id) WHERE status = 'active' stops two leases on one unit.
    /// </summary>
    async Task<IActionResult?> ActivateCoreAsync(Lease l, bool overrideBlacklist, CancellationToken ct)
    {
        if (l.Status != "draft") return this.Error($"The lease is already {l.Status}");
        var tenant = await db.Tenants.FirstAsync(t => t.Id == l.TenantId, ct);
        if (tenant.Blacklisted && !overrideBlacklist)
            return this.Error($"{tenant.FullName} is blacklisted ({tenant.BlacklistReason}). Tick the override to let to them anyway.");
        var unit = await db.Units.FirstAsync(u => u.Id == l.UnitId, ct);
        if (unit.Status is not ("vacant" or "reserved") || await db.Leases.AnyAsync(x => x.UnitId == l.UnitId && x.Status == "active" && x.Id != l.Id, ct))
            return Conflict(new { error = $"Unit {unit.Code} is no longer vacant" });

        var today = Today;
        l.Status = "active";
        unit.Status = "occupied";
        if (l.DepositAmount > 0)
            db.Deposits.Add(new Deposit { TenantId = l.TenantId, LeaseId = l.Id, DepositType = "rent", CurrencyCode = l.CurrencyCode, AmountHeld = 0, Status = "held" });
        if (l.WaterDepositAmount > 0)
            db.Deposits.Add(new Deposit { TenantId = l.TenantId, LeaseId = l.Id, DepositType = "water", CurrencyCode = l.CurrencyCode, AmountHeld = 0, Status = "held" });

        var rows = await db.RentSchedules.Where(r => r.LeaseId == l.Id).OrderBy(r => r.EffectiveFrom).ToListAsync(ct);
        var rentCodes = await schedules.RentCodesAsync(ct);
        // The base row and rows already in effect (a back-dated lease) are current/completed; the rest wait for notice.
        var inEffect = rows.Where((r, i) => i == 0 || (r.EffectiveFrom <= today && r.MonthlyRent is not null)).ToList();
        foreach (var r in inEffect)
        {
            r.ApprovedBy ??= User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            RentScheduleService.MakeCurrent(l, rows, r, rentCodes);
        }

        await bcQueue.EnqueueUpsertTenantAsync(tenant, ct);
        if (await this.TrySaveAsync(db, ct) is { } fail) return fail;
        // EnqueueUpsertTenantAsync reads the active lease from the database, so refresh the payload now it is saved.
        await bcQueue.EnqueueUpsertTenantAsync(tenant, ct);
        await db.SaveChangesAsync(ct);
        await schedules.AdvanceAsync(today, l.Id, ct);
        return null;
    }

    // ---------------------------------------------------------------- renewal and termination

    /// <summary>Extends the end date and keeps the schedule; the escalation rule adds rows for the new term.</summary>
    [HttpPost("{id:guid}/renew"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> Renew(Guid id, RenewInput input, CancellationToken ct)
    {
        var l = await Visible().Include(x => x.Charges).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound();
        if (l.Status != "active") return this.Error("Only active leases can be renewed");
        var oldEnd = l.EndDate ?? RentScheduleGenerator.OpenEndedEnd(l.StartDate);
        if (input.NewEndDate <= oldEnd) return this.Error($"The new end date must be after {oldEnd:d MMM yyyy}");

        var rows = await db.RentSchedules.Where(r => r.LeaseId == id).OrderBy(r => r.EffectiveFrom).ToListAsync(ct);
        var rule = await db.RentEscalationRules.AsNoTracking().FirstOrDefaultAsync(r => r.LeaseId == id, ct);
        var last = rows.LastOrDefault();
        var added = new List<(DateOnly From, decimal? Rent, string Label)>();
        if (rule is not null && rule.Type != "stepped" && rows.Count > 0)
        {
            var baseRent = rows[0].MonthlyRent ?? 0;
            var lastKnown = rows.LastOrDefault(r => r.MonthlyRent is not null)?.MonthlyRent ?? baseRent;
            var n = 1;
            while (l.StartDate.AddMonths(n * rule.EveryMonths) <= last!.EffectiveFrom) n++;
            added = RentScheduleGenerator.Continue(RentScheduleGenerator.ToInput(rule, rows), baseRent, l.StartDate, n, lastKnown, input.NewEndDate);
        }
        var closed = RentScheduleGenerator.Close([.. rows.Select(r => (r.EffectiveFrom, r.MonthlyRent, r.ChangeLabel ?? "")), .. added], input.NewEndDate);
        if (last is not null) last.EffectiveTo = closed[rows.Count - 1].To;
        foreach (var r in closed.Skip(rows.Count))
            db.RentSchedules.Add(new RentSchedule { LeaseId = id, EffectiveFrom = r.From, EffectiveTo = r.To, MonthlyRent = r.MonthlyRent, ChangeLabel = r.Label, Status = "scheduled" });
        l.EndDate = input.NewEndDate;
        if (await this.TrySaveAsync(db, ct) is { } fail) return fail;
        await schedules.AdvanceAsync(Today, id, ct);
        return Ok(new { l.Id, l.EndDate, added = closed.Count - rows.Count });
    }

    /// <summary>
    /// Ends the lease on the given date: "ended" on or after the end date, "terminated" before it.
    /// The unit becomes vacant and schedule rows after the date are dropped. Deposits stay held until the
    /// move-out (stage 5) refunds or applies them.
    /// </summary>
    [HttpPost("{id:guid}/terminate"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> Terminate(Guid id, TerminateInput input, CancellationToken ct)
    {
        var l = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound();
        if (l.Status != "active") return this.Error("Only active leases can be ended");
        if (string.IsNullOrWhiteSpace(input.Reason)) return this.Error("Give a reason");
        if (input.EndDate < l.StartDate) return this.Error("The end date cannot be before the lease start");

        var rows = await db.RentSchedules.Where(r => r.LeaseId == id).ToListAsync(ct);
        foreach (var r in rows.Where(r => r.EffectiveFrom > input.EndDate)) db.RentSchedules.Remove(r);
        foreach (var r in rows.Where(r => r.EffectiveFrom <= input.EndDate))
        {
            if (r.EffectiveTo is null || r.EffectiveTo > input.EndDate) r.EffectiveTo = input.EndDate;
            if (r.Status == "current") r.Status = "completed";
        }
        l.Status = l.EndDate is null || input.EndDate >= l.EndDate ? "ended" : "terminated";
        l.EndDate = input.EndDate;
        l.EndReason = input.Reason.Trim()[..Math.Min(200, input.Reason.Trim().Length)];
        l.EndedAt = clock.Now();
        var unit = await db.Units.FirstAsync(u => u.Id == l.UnitId, ct);
        if (unit.Status == "occupied") unit.Status = "vacant";
        return await this.TrySaveAsync(db, ct) ?? Ok(new { l.Id, l.Status, l.EndDate });
    }

    // ---------------------------------------------------------------- schedule approvals (accountant)

    async Task<(Lease? Lease, List<RentSchedule> Rows, RentSchedule? Row)> LoadRowAsync(Guid id, Guid rowId, CancellationToken ct)
    {
        var l = await Visible().Include(x => x.Charges).Include(x => x.Tenant).Include(x => x.Unit).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return (null, [], null);
        var rows = await db.RentSchedules.Where(r => r.LeaseId == id).OrderBy(r => r.EffectiveFrom).ToListAsync(ct);
        return (l, rows, rows.FirstOrDefault(r => r.Id == rowId));
    }

    /// <summary>"Approve &amp; send notice": the row is approved, the tenant gets the escalation SMS and the row becomes current on its date.</summary>
    [HttpPost("{id:guid}/schedule/{rowId:guid}/approve"), Authorize(Policy = Policies.Approvals)]
    public async Task<IActionResult> Approve(Guid id, Guid rowId, RentInput? input, CancellationToken ct)
    {
        var (l, rows, row) = await LoadRowAsync(id, rowId, ct);
        if (l is null || row is null) return NotFound();
        if (l.Status != "active") return this.Error("The lease is not active");
        if (row.Status is not ("pending_approval" or "scheduled")) return this.Error($"This row is already {row.Status.Replace('_', ' ')}");
        if (input?.MonthlyRent is { } rent)
        {
            if (rent <= 0) return this.Error("Monthly rent must be more than 0");
            row.MonthlyRent = RentScheduleGenerator.Round(rent);
        }
        if (row.MonthlyRent is null) return this.Error("Enter the agreed rent for this market review first");

        await schedules.ApproveRowAsync(l, rows, row, User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value, ct);
        if (row.EffectiveFrom <= Today) RentScheduleService.MakeCurrent(l, rows, row, await schedules.RentCodesAsync(ct));
        return await this.TrySaveAsync(db, ct) ?? Ok(new { row.Id, row.Status, row.MonthlyRent, row.NoticeSentAt });
    }

    /// <summary>Changes the amount of an escalation that has not been approved yet.</summary>
    [HttpPut("{id:guid}/schedule/{rowId:guid}"), Authorize(Policy = Policies.Approvals)]
    public async Task<IActionResult> EditRow(Guid id, Guid rowId, RentInput input, CancellationToken ct)
    {
        var (l, _, row) = await LoadRowAsync(id, rowId, ct);
        if (l is null || row is null) return NotFound();
        if (row.Status is not ("pending_approval" or "scheduled")) return this.Error("Only escalations that are not yet approved can change");
        if (input.MonthlyRent is not > 0) return this.Error("Monthly rent must be more than 0");
        row.MonthlyRent = RentScheduleGenerator.Round(input.MonthlyRent.Value);
        row.ChangeLabel = row.ChangeLabel == "Market review" ? "Market review" : "Adjusted";
        return await this.TrySaveAsync(db, ct) ?? Ok(new { row.Id, row.MonthlyRent });
    }

    /// <summary>Declines an escalation: the row is removed and the previous rent runs on.</summary>
    [HttpDelete("{id:guid}/schedule/{rowId:guid}"), Authorize(Policy = Policies.Approvals)]
    public async Task<IActionResult> Decline(Guid id, Guid rowId, CancellationToken ct)
    {
        var (l, rows, row) = await LoadRowAsync(id, rowId, ct);
        if (l is null || row is null) return NotFound();
        if (row.Status is not ("pending_approval" or "scheduled") || rows.IndexOf(row) == 0) return this.Error("Only escalations that are not yet approved can be declined");
        var prev = rows[rows.IndexOf(row) - 1];
        prev.EffectiveTo = row.EffectiveTo;
        db.RentSchedules.Remove(row);
        return await this.TrySaveAsync(db, ct) ?? NoContent();
    }
}
