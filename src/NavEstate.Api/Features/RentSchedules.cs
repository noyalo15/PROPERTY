using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NavEstate.Api.Common;
using NavEstate.Api.Data;
using NavEstate.Api.Domain;

namespace NavEstate.Api.Features;

public record StepInput(DateOnly From, decimal MonthlyRent);

/// <summary>Escalation rule as typed in the lease wizard. <see cref="Steps"/> is used only by "stepped".</summary>
public record EscalationInput(string Type, decimal Value, int EveryMonths = 12, bool Compounding = true, int NoticeDays = 90,
    bool RequiresApproval = true, List<StepInput>? Steps = null);

public record ScheduleRow(DateOnly From, DateOnly? To, decimal? MonthlyRent, string Label);

/// <summary>
/// Business rule 2: rent_schedule rows for the whole lease term.
/// fixed_percent: previous × (1 + v/100) when compounding, base × (1 + n·v/100) when not;
/// fixed_amount: previous + v; stepped: the typed rows; market_review: a row with no amount.
/// Amounts are rounded to 2 decimals, halves away from zero (4,862.025 → 4,862.03).
/// </summary>
public static class RentScheduleGenerator
{
    /// <summary>A lease with no end date gets rows for this many years; renewals add more.</summary>
    public const int OpenEndedYears = 5;
    public const int MarketReviewAlertDays = 60;

    public static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public static string? Validate(EscalationInput? r, DateOnly start, DateOnly? end)
    {
        if (r is null) return null;
        var steps = r.Steps ?? [];
        return Api.First(
            Api.In(r.Type, V.EscalationType, "escalation type"),
            r.Type != "stepped" && (r.EveryMonths < 1 || r.EveryMonths > 120) ? "Escalate every 1 to 120 months" : null,
            r.NoticeDays < 0 || r.NoticeDays > 365 ? "Notice days must be between 0 and 365" : null,
            r.Type == "fixed_percent" && (r.Value <= 0 || r.Value > 100) ? "Escalation percentage must be more than 0 and at most 100" : null,
            r.Type == "fixed_amount" && r.Value <= 0 ? "Escalation amount must be more than 0" : null,
            r.Type == "stepped" && steps.Count == 0 ? "Add at least one step (date and new monthly rent)" : null,
            r.Type == "stepped" && steps.Any(s => s.MonthlyRent <= 0) ? "Each step needs a monthly rent above 0" : null,
            r.Type == "stepped" && steps.Any(s => s.From <= start || (end is not null && s.From > end)) ? "Each step must start after the lease start date and on or before the end date" : null,
            r.Type == "stepped" && steps.Select(s => s.From).Distinct().Count() != steps.Count ? "Two steps have the same date" : null);
    }

    public static DateOnly OpenEndedEnd(DateOnly start) => start.AddYears(OpenEndedYears).AddDays(-1);
    static DateOnly Horizon(DateOnly start, DateOnly? end) => end ?? OpenEndedEnd(start);

    /// <summary>Rows for a new lease: the base rent from the start date, then one row per escalation.</summary>
    public static List<ScheduleRow> Generate(decimal baseRent, DateOnly start, DateOnly? end, EscalationInput? rule)
    {
        var rows = new List<(DateOnly From, decimal? Rent, string Label)> { (start, Round(baseRent), "Base rent") };
        if (rule is not null) rows.AddRange(Continue(rule, baseRent, start, 1, baseRent, Horizon(start, end)));
        return Close(rows, end);
    }

    /// <summary>
    /// Further rows after a renewal: escalation n = <paramref name="fromIndex"/> onwards, continuing from
    /// <paramref name="previousRent"/>, up to the new end date.
    /// </summary>
    public static List<(DateOnly From, decimal? Rent, string Label)> Continue(EscalationInput rule, decimal baseRent, DateOnly start,
        int fromIndex, decimal previousRent, DateOnly horizon)
    {
        var rows = new List<(DateOnly, decimal?, string)>();
        if (rule.Type == "stepped")
        {
            foreach (var s in (rule.Steps ?? []).OrderBy(s => s.From).Where(s => s.From <= horizon))
                rows.Add((s.From, Round(s.MonthlyRent), "Step"));
            return rows;
        }
        var prev = previousRent;
        for (var n = fromIndex; ; n++)
        {
            var from = start.AddMonths(n * rule.EveryMonths);
            if (from > horizon) break;
            switch (rule.Type)
            {
                case "fixed_percent":
                    prev = rule.Compounding ? Round(prev * (1 + rule.Value / 100m)) : Round(baseRent * (1 + n * rule.Value / 100m));
                    rows.Add((from, prev, $"+{rule.Value.ToString("0.##", CultureInfo.InvariantCulture)}%"));
                    break;
                case "fixed_amount":
                    prev = Round(prev + rule.Value);
                    rows.Add((from, prev, $"+{rule.Value.ToString("#,##0.##", CultureInfo.InvariantCulture)}"));
                    break;
                case "market_review":
                    rows.Add((from, null, "Market review"));
                    break;
            }
        }
        return rows;
    }

    /// <summary>Sets each row's end to the day before the next row, and the last row's end to the lease end.</summary>
    public static List<ScheduleRow> Close(List<(DateOnly From, decimal? Rent, string Label)> rows, DateOnly? end)
    {
        var sorted = rows.OrderBy(r => r.From).ToList();
        return sorted.Select((r, i) => new ScheduleRow(r.From, i + 1 < sorted.Count ? sorted[i + 1].From.AddDays(-1) : end, r.Rent, r.Label)).ToList();
    }

    /// <summary>When a row turns "notice due": notice_days before it starts (60 days for a market review).</summary>
    public static DateOnly NoticeDate(RentSchedule row, RentEscalationRule? rule) =>
        row.EffectiveFrom.AddDays(-(rule?.Type == "market_review" ? MarketReviewAlertDays : rule?.NoticeDays ?? 0));

    public static EscalationInput ToInput(RentEscalationRule r, IEnumerable<RentSchedule> rows) =>
        new(r.Type, r.Value, r.EveryMonths, r.Compounding, r.NoticeDays, r.RequiresApproval,
            r.Type == "stepped" ? rows.OrderBy(x => x.EffectiveFrom).Skip(1).Where(x => x.MonthlyRent is not null).Select(x => new StepInput(x.EffectiveFrom, x.MonthlyRent!.Value)).ToList() : null);
}

/// <summary>
/// Moves rent_schedule rows through scheduled → pending_approval (notice due) → approved → current → completed,
/// sends the escalation notice on approval and keeps the RENT lease charge equal to the current row.
/// Unapproved escalations never take effect.
/// </summary>
public class RentScheduleService(NavEstateDbContext db, ISmsSender sms, TimeProvider clock, ILogger<RentScheduleService> log)
{
    public const string SystemUser = "system";

    public record DailySummary(int NoticesDue, int AutoApproved, int MadeCurrent);

    /// <summary>Hangfire entry point (daily 00:30 Nairobi).</summary>
    public async Task<DailySummary> RunDailyAsync(CancellationToken ct)
    {
        var s = await AdvanceAsync(clock.Today(), null, ct);
        log.LogInformation("Rent schedule: {Due} notices due, {Auto} auto-approved, {Current} escalations applied", s.NoticesDue, s.AutoApproved, s.MadeCurrent);
        return s;
    }

    /// <summary>Applies the daily transitions for every active lease (or one lease) and saves.</summary>
    public async Task<DailySummary> AdvanceAsync(DateOnly today, Guid? leaseId, CancellationToken ct)
    {
        var leases = await db.Leases.Where(l => l.Status == "active" && (leaseId == null || l.Id == leaseId))
            .Where(l => db.RentSchedules.Any(r => r.LeaseId == l.Id && (r.Status == "scheduled" || r.Status == "approved")))
            .Include(l => l.Charges).Include(l => l.Tenant).Include(l => l.Unit)
            .ToListAsync(ct);
        int due = 0, auto = 0, current = 0;
        if (leases.Count == 0) return new(0, 0, 0);
        var ids = leases.Select(l => l.Id).ToList();
        var rules = await db.RentEscalationRules.Where(r => ids.Contains(r.LeaseId)).ToDictionaryAsync(r => r.LeaseId, ct);
        var allRows = await db.RentSchedules.Where(r => ids.Contains(r.LeaseId)).ToListAsync(ct);
        var rentCodes = await RentCodesAsync(ct);

        foreach (var lease in leases)
        {
            rules.TryGetValue(lease.Id, out var rule);
            var rows = allRows.Where(r => r.LeaseId == lease.Id).OrderBy(r => r.EffectiveFrom).ToList();
            foreach (var row in rows.Where(r => r.Status == "scheduled"))
            {
                if (RentScheduleGenerator.NoticeDate(row, rule) > today) continue;
                if (rule is { RequiresApproval: false } && row.MonthlyRent is not null)
                {
                    await ApproveRowAsync(lease, rows, row, SystemUser, ct);
                    auto++;
                }
                else
                {
                    row.Status = "pending_approval";
                    due++;
                }
            }
            foreach (var row in rows.Where(r => r.Status == "approved" && r.EffectiveFrom <= today).ToList())
            {
                MakeCurrent(lease, rows, row, rentCodes);
                current++;
            }
        }
        await db.SaveChangesAsync(ct);
        return new(due, auto, current);
    }

    public async Task<HashSet<string>> RentCodesAsync(CancellationToken ct) =>
        (await db.ChargeCodes.AsNoTracking().Where(c => c.IsRent).Select(c => c.Code).ToListAsync(ct)).ToHashSet();

    /// <summary>The row becomes current, the previous current row is completed and the RENT charge follows.</summary>
    public static void MakeCurrent(Lease lease, List<RentSchedule> rows, RentSchedule row, ISet<string> rentCodes)
    {
        foreach (var other in rows.Where(r => r != row && r.Status == "current"))
        {
            other.Status = "completed";
            if (other.EffectiveTo is null || other.EffectiveTo >= row.EffectiveFrom) other.EffectiveTo = row.EffectiveFrom.AddDays(-1);
        }
        row.Status = "current";
        if (row.MonthlyRent is { } rent)
            foreach (var c in lease.Charges.Where(c => c.Active && rentCodes.Contains(c.ChargeCode)).Take(1))
                c.Amount = rent;
    }

    /// <summary>Sets the row approved, records who approved it and sends the SMS notice (written to ops.notices).</summary>
    public async Task ApproveRowAsync(Lease lease, List<RentSchedule> rows, RentSchedule row, string approvedBy, CancellationToken ct)
    {
        row.Status = "approved";
        row.ApprovedBy = approvedBy;
        row.NoticeSentAt = clock.Now();
        var previous = rows.Where(r => r.EffectiveFrom < row.EffectiveFrom && r.MonthlyRent is not null).OrderBy(r => r.EffectiveFrom).LastOrDefault();
        await SendNoticeAsync(lease, previous?.MonthlyRent, row, ct);
    }

    async Task SendNoticeAsync(Lease lease, decimal? oldRent, RentSchedule row, CancellationToken ct)
    {
        var tenant = lease.Tenant ?? await db.Tenants.FindAsync([lease.TenantId], ct);
        var unit = lease.Unit ?? await db.Units.FindAsync([lease.UnitId], ct);
        if (tenant is null || unit is null) return;
        var template = await db.NotificationTemplates.AsNoTracking()
            .Where(t => t.Code == "escalation_notice" && t.Channel == "sms" && t.Active).Select(t => t.Body).FirstOrDefaultAsync(ct)
            ?? "Dear {{tenant_name}}, as per your lease {{lease_no}}, rent for {{unit}} changes from {{old_rent}} to {{new_rent}} from {{effective_from}}.";
        var body = Render(template, new()
        {
            ["tenant_name"] = tenant.FullName, ["lease_no"] = lease.LeaseNo, ["unit"] = unit.Code,
            ["old_rent"] = Money(oldRent, lease.CurrencyCode), ["new_rent"] = Money(row.MonthlyRent, lease.CurrencyCode),
            ["effective_from"] = row.EffectiveFrom.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
        });
        var notice = new Notice { TenantId = tenant.Id, PropertyId = unit.PropertyId, Channel = "sms", Subject = $"Rent escalation notice {lease.LeaseNo}", Body = body };
        try
        {
            var ok = await sms.SendAsync(tenant.Phone, body, ct);
            notice.Status = ok ? "sent" : "failed";
            notice.SentAt = ok ? clock.Now() : null;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Escalation notice SMS to {Tenant} failed", tenant.TenantNo);
            notice.Status = "failed";
        }
        db.Notices.Add(notice);
    }

    public static string Money(decimal? v, string currency) =>
        v is null ? "(to be agreed)" : $"{currency} {v.Value.ToString("#,##0.00", CultureInfo.InvariantCulture)}";

    public static string Render(string template, Dictionary<string, string> values) =>
        values.Aggregate(template, (s, kv) => s.Replace("{{" + kv.Key + "}}", kv.Value));
}
