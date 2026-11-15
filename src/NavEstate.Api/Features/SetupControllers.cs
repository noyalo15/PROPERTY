using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NavEstate.Api.Auth;
using NavEstate.Api.Data;
using NavEstate.Api.Domain;

namespace NavEstate.Api.Features;

[Route("api/setup/charge-codes")]
public class ChargeCodesController(NavEstateDbContext db, IAuthorizationService a) : CrudController<ChargeCode, string>(db, a)
{
    protected override IQueryable<ChargeCode> Ordered(IQueryable<ChargeCode> q) => q.OrderBy(x => x.AllocationPriority).ThenBy(x => x.Code);
    protected override string KeyOf(ChargeCode e) => e.Code;
    protected override void SetKey(ChargeCode e, string key) => e.Code = key;
    protected override string? Validate(ChargeCode e)
    {
        e.Code = e.Code.Trim().ToUpperInvariant();
        return e.Code.Length == 0 || e.Code.Any(ch => ch is ';' or '|' or ' ') ? "Code is required and cannot contain spaces, ; or |" : null;
    }
}

[Route("api/setup/penalty-rules")]
public class PenaltyRulesController(NavEstateDbContext db, IAuthorizationService a) : CrudController<PenaltyRule, Guid>(db, a)
{
    protected override IQueryable<PenaltyRule> Ordered(IQueryable<PenaltyRule> q) => q.OrderBy(x => x.Name);
    protected override Guid KeyOf(PenaltyRule e) => e.Id;
    protected override void SetKey(PenaltyRule e, Guid key) => e.Id = key;
    protected override string? Validate(PenaltyRule e) => Api.First(
        Api.In(e.Basis, V.PenaltyBasis, "basis"), Api.In(e.Frequency, V.PenaltyFrequency, "frequency"), Api.In(e.CapType, V.PenaltyCap, "cap_type"),
        e.CapType != "none" && e.CapValue is null ? "cap_value is required when a cap is set" : null,
        e.Basis != "fixed" && e.Value > 100 ? "A percentage penalty cannot exceed 100" : null);
}

[Route("api/setup/payment-channels")]
public class PaymentChannelsController(NavEstateDbContext db, IAuthorizationService a) : CrudController<PaymentChannel, Guid>(db, a)
{
    protected override IQueryable<PaymentChannel> Ordered(IQueryable<PaymentChannel> q) => q.OrderBy(x => x.Code);
    protected override Guid KeyOf(PaymentChannel e) => e.Id;
    protected override void SetKey(PaymentChannel e, Guid key) => e.Id = key;
    protected override string? Validate(PaymentChannel e)
    {
        e.Code = e.Code.Trim().ToUpperInvariant();
        return Api.First(Api.In(e.BcChannelCode, V.BcChannel, "bc_channel_code"), Api.In(e.CurrencyCode, V.Currency, "currency_code"));
    }
}

[Route("api/setup/templates")]
public class TemplatesController(NavEstateDbContext db, IAuthorizationService a) : CrudController<NotificationTemplate, Guid>(db, a)
{
    protected override IQueryable<NotificationTemplate> Ordered(IQueryable<NotificationTemplate> q) => q.OrderBy(x => x.Code).ThenBy(x => x.Channel);
    protected override Guid KeyOf(NotificationTemplate e) => e.Id;
    protected override void SetKey(NotificationTemplate e, Guid key) => e.Id = key;
    protected override string? Validate(NotificationTemplate e) => Api.First(
        Api.In(e.Channel, V.NoticeChannel, "channel"),
        e.Channel == "sms" && e.Body.Length > 480 ? "SMS templates are limited to 480 characters (3 SMS)" : null);
}

[Route("api/setup/currencies")]
public class CurrenciesController(NavEstateDbContext db, IAuthorizationService a) : CrudController<Currency, string>(db, a)
{
    protected override IQueryable<Currency> Ordered(IQueryable<Currency> q) => q.OrderBy(x => x.Code);
    protected override string KeyOf(Currency e) => e.Code;
    protected override void SetKey(Currency e, string key) => e.Code = key;
    protected override string? Validate(Currency e)
    {
        e.Code = e.Code.Trim().ToUpperInvariant();
        return Api.In(e.Code, V.Currency, "code");
    }
}

[ApiController, Route("api/setup/exchange-rates"), Authorize(Policy = Policies.Staff)]
public class ExchangeRatesController(NavEstateDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await db.ExchangeRates.AsNoTracking().OrderByDescending(x => x.RateDate).Take(60).ToListAsync(ct));

    public record RateInput(string CurrencyCode, DateOnly RateDate, decimal KesPerUnit);

    /// <summary>Manual rate (source MANUAL) for when BC is unavailable; the daily job overwrites it with BC's.</summary>
    [HttpPost, Authorize(Policy = Policies.Setup)]
    public async Task<IActionResult> Upsert(RateInput r, CancellationToken ct)
    {
        if (r.CurrencyCode == "KES" || Api.In(r.CurrencyCode, V.Currency, "currency_code") is { } err) return this.Error("Rates are kept for USD only");
        if (r.KesPerUnit <= 0) return this.Error("Rate must be positive");
        var row = await db.ExchangeRates.FirstOrDefaultAsync(x => x.CurrencyCode == r.CurrencyCode && x.RateDate == r.RateDate, ct);
        if (row is null) db.ExchangeRates.Add(row = new ExchangeRate { CurrencyCode = r.CurrencyCode, RateDate = r.RateDate });
        row.KesPerUnit = r.KesPerUnit; row.Source = "MANUAL";
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }
}

[ApiController, Route("api/setup/water-tariffs"), Authorize(Policy = Policies.Staff)]
public class WaterTariffsController(NavEstateDbContext db) : ControllerBase
{
    public record BandInput(decimal FromM3, decimal? ToM3, decimal Rate);
    public record TariffInput(Guid? PropertyId, string Name, decimal StandingCharge, DateOnly EffectiveFrom, List<BandInput> Bands);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await db.WaterTariffs.AsNoTracking().Include(t => t.Bands.OrderBy(b => b.FromM3)).OrderBy(t => t.Name).ThenByDescending(t => t.EffectiveFrom).ToListAsync(ct));

    /// <summary>Bands must start at 0, be contiguous and only the last may be open-ended.</summary>
    public static string? ValidateBands(List<BandInput> bands)
    {
        if (bands.Count == 0) return "At least one band is required";
        var ordered = bands.OrderBy(b => b.FromM3).ToList();
        if (ordered[0].FromM3 != 0) return "The first band must start at 0 m³";
        for (var i = 0; i < ordered.Count; i++)
        {
            var b = ordered[i];
            if (b.Rate < 0) return "Rates cannot be negative";
            var last = i == ordered.Count - 1;
            if (!last && b.ToM3 is null) return "Only the last band can be open-ended";
            if (b.ToM3 is { } to && to <= b.FromM3) return "Each band must end after it starts";
            if (!last && ordered[i + 1].FromM3 != b.ToM3) return "Bands must be contiguous (each band starts where the previous ends)";
        }
        return null;
    }

    [HttpPost, Authorize(Policy = Policies.Setup)]
    public async Task<IActionResult> Create(TariffInput input, CancellationToken ct)
    {
        if (ValidateBands(input.Bands) is { } err) return this.Error(err);
        var t = new WaterTariff { PropertyId = input.PropertyId, Name = input.Name, StandingCharge = input.StandingCharge, EffectiveFrom = input.EffectiveFrom };
        t.Bands = input.Bands.Select(b => new WaterTariffBand { FromM3 = b.FromM3, ToM3 = b.ToM3, Rate = b.Rate }).ToList();
        db.WaterTariffs.Add(t);
        return await this.TrySaveAsync(db, ct) ?? Ok(t);
    }

    [HttpPut("{id:guid}"), Authorize(Policy = Policies.Setup)]
    public async Task<IActionResult> Update(Guid id, TariffInput input, CancellationToken ct)
    {
        if (ValidateBands(input.Bands) is { } err) return this.Error(err);
        var t = await db.WaterTariffs.Include(x => x.Bands).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        t.PropertyId = input.PropertyId; t.Name = input.Name; t.StandingCharge = input.StandingCharge; t.EffectiveFrom = input.EffectiveFrom;
        db.WaterTariffBands.RemoveRange(t.Bands);
        t.Bands = input.Bands.Select(b => new WaterTariffBand { TariffId = id, FromM3 = b.FromM3, ToM3 = b.ToM3, Rate = b.Rate }).ToList();
        return await this.TrySaveAsync(db, ct) ?? Ok(t);
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = Policies.Setup)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var t = await db.WaterTariffs.FindAsync([id], ct);
        if (t is null) return NotFound();
        db.WaterTariffs.Remove(t);
        return await this.TrySaveAsync(db, ct) ?? NoContent();
    }
}

/// <summary>Company settings: key → JSON value (company name, Paybill, WHT rate, MRI rate, ...).</summary>
[ApiController, Route("api/setup/settings"), Authorize(Policy = Policies.Staff)]
public class SettingsController(NavEstateDbContext db) : ControllerBase
{
    public static readonly string[] Known =
    [
        "company_name", "company_logo", "company_address", "company_kra_pin", "paybill_number", "allocation_order",
        "vat_rate", "wht_rate", "mri_rate", "penalty_run_time", "reminder_days", "sms_sender_id"
    ];

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var rows = await db.Settings.AsNoTracking().ToListAsync(ct);
        return Ok(rows.ToDictionary(r => r.Key, r => JsonDocument.Parse(r.Value).RootElement));
    }

    [HttpPut, Authorize(Policy = Policies.Setup)]
    public async Task<IActionResult> Put(Dictionary<string, JsonElement> values, CancellationToken ct)
    {
        var unknown = values.Keys.Where(k => !Known.Contains(k)).ToList();
        if (unknown.Count > 0) return this.Error($"Unknown setting(s): {string.Join(", ", unknown)}");
        var rows = await db.Settings.Where(s => values.Keys.Contains(s.Key)).ToListAsync(ct);
        foreach (var (key, value) in values)
        {
            var row = rows.FirstOrDefault(r => r.Key == key);
            if (row is null) db.Settings.Add(row = new Setting { Key = key });
            row.Value = value.GetRawText();
        }
        await db.SaveChangesAsync(ct);
        return await Get(ct);
    }
}
