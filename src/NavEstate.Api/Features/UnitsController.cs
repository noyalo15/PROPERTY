using System.Globalization;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NavEstate.Api.Auth;
using NavEstate.Api.Data;
using NavEstate.Api.Domain;

namespace NavEstate.Api.Features;

[ApiController, Route("api/units"), Authorize(Policy = Policies.Staff)]
public class UnitsController(NavEstateDbContext db) : ControllerBase
{
    public record UnitInput(Guid PropertyId, Guid? BlockId, string Code, string UnitType, string? Floor, decimal? AreaSqft,
        string Status, decimal DefaultRent, string DefaultCurrency);
    public record ChargeInput(string ChargeCode, decimal Amount, string CurrencyCode, string Frequency);
    public record MeterInput(string MeterType, string SerialNo, decimal? LastReading, DateOnly? LastReadingDate, bool Active = true);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var u = await db.Units.AsNoTracking().Include(x => x.Property).Include(x => x.Block)
            .Include(x => x.Charges).Include(x => x.Meters).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound();
        var lease = await db.Leases.AsNoTracking().Where(l => l.UnitId == id && l.Status == "active")
            .Select(l => new { l.Id, l.LeaseNo, l.StartDate, l.EndDate, TenantId = l.Tenant!.Id, l.Tenant.TenantNo, l.Tenant.FullName }).FirstOrDefaultAsync(ct);
        return Ok(new
        {
            u.Id, u.PropertyId, Property = u.Property!.Name, PropertyCode = u.Property.Code, u.BlockId, Block = u.Block?.Name,
            u.Code, u.UnitType, u.Floor, u.AreaSqft, u.Status, u.DefaultRent, u.DefaultCurrency,
            Charges = u.Charges.OrderBy(c => c.ChargeCode).Select(c => new { c.Id, c.ChargeCode, c.Amount, c.CurrencyCode, c.Frequency }),
            Meters = u.Meters.OrderBy(m => m.MeterType).Select(m => new { m.Id, m.MeterType, m.SerialNo, m.LastReading, m.LastReadingDate, m.Active }),
            ActiveLease = lease
        });
    }

    string? Validate(UnitInput i) => Api.First(
        string.IsNullOrWhiteSpace(i.Code) || i.Code.Length > 20 ? "Code is required (max 20 characters; it becomes the BC UNIT dimension value)" : null,
        Api.In(i.UnitType, V.UnitType, "unit_type"), Api.In(i.Status, V.UnitStatus, "status"), Api.In(i.DefaultCurrency, V.Currency, "default_currency"),
        i.DefaultRent < 0 ? "Rent cannot be negative" : null);

    [HttpPost, Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> Create(UnitInput input, CancellationToken ct)
    {
        if (Validate(input) is { } err) return this.Error(err);
        if (!await db.Properties.AnyAsync(p => p.Id == input.PropertyId, ct)) return this.Error("Property not found");
        if (input.Status == "occupied") return this.Error("A unit becomes occupied when a lease is activated");
        var u = new Unit { PropertyId = input.PropertyId };
        Apply(u, input);
        db.Units.Add(u);
        return await this.TrySaveAsync(db, ct) ?? Ok(new { u.Id });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> Update(Guid id, UnitInput input, CancellationToken ct)
    {
        if (Validate(input) is { } err) return this.Error(err);
        var u = await db.Units.FindAsync([id], ct);
        if (u is null) return NotFound();
        var hasActiveLease = await db.Leases.AnyAsync(l => l.UnitId == id && l.Status == "active", ct);
        if (hasActiveLease && input.Status != "occupied") return this.Error("The unit has an active lease; end or transfer the lease to change its status");
        if (!hasActiveLease && input.Status == "occupied") return this.Error("A unit becomes occupied when a lease is activated");
        if (hasActiveLease && !string.Equals(u.Code, input.Code.Trim(), StringComparison.OrdinalIgnoreCase))
            return this.Error("The code cannot change while the unit is let: it is the tenant's BC UNIT dimension");
        Apply(u, input with { PropertyId = u.PropertyId });
        return await this.TrySaveAsync(db, ct) ?? Ok(new { u.Id });
    }

    static void Apply(Unit u, UnitInput i)
    {
        u.BlockId = i.BlockId; u.Code = i.Code.Trim().ToUpperInvariant(); u.UnitType = i.UnitType; u.Floor = i.Floor;
        u.AreaSqft = i.AreaSqft; u.Status = i.Status; u.DefaultRent = i.DefaultRent; u.DefaultCurrency = i.DefaultCurrency;
    }

    /// <summary>Replaces the unit's default charges (copied to new leases).</summary>
    [HttpPut("{id:guid}/charges"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> SetCharges(Guid id, List<ChargeInput> charges, CancellationToken ct)
    {
        var u = await db.Units.Include(x => x.Charges).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound();
        foreach (var c in charges)
            if (Api.First(Api.In(c.CurrencyCode, V.Currency, "currency_code"), Api.In(c.Frequency, V.ChargeFrequency, "frequency"),
                    c.Amount < 0 ? "Amounts cannot be negative" : null) is { } err) return this.Error(err);
        var codes = charges.Select(c => c.ChargeCode).Distinct().ToList();
        var known = await db.ChargeCodes.Where(c => codes.Contains(c.Code)).Select(c => c.Code).ToListAsync(ct);
        if (codes.Except(known).FirstOrDefault() is { } missing) return this.Error($"Unknown charge code {missing}");
        db.UnitCharges.RemoveRange(u.Charges);
        foreach (var c in charges)
            db.UnitCharges.Add(new UnitCharge { UnitId = id, ChargeCode = c.ChargeCode, Amount = c.Amount, CurrencyCode = c.CurrencyCode, Frequency = c.Frequency });
        return await this.TrySaveAsync(db, ct) ?? NoContent();
    }

    [HttpPost("{id:guid}/meters"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> AddMeter(Guid id, MeterInput m, CancellationToken ct)
    {
        if (!await db.Units.AnyAsync(u => u.Id == id, ct)) return NotFound();
        if (Api.In(m.MeterType, V.MeterType, "meter_type") is { } err) return this.Error(err);
        if (string.IsNullOrWhiteSpace(m.SerialNo)) return this.Error("Serial number is required");
        var meter = new Meter { UnitId = id, MeterType = m.MeterType, SerialNo = m.SerialNo.Trim(), LastReading = m.LastReading, LastReadingDate = m.LastReadingDate, Active = m.Active };
        db.Meters.Add(meter);
        return await this.TrySaveAsync(db, ct) ?? Ok(new { meter.Id });
    }

    [HttpPut("{id:guid}/meters/{meterId:guid}"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> UpdateMeter(Guid id, Guid meterId, MeterInput m, CancellationToken ct)
    {
        var meter = await db.Meters.FirstOrDefaultAsync(x => x.Id == meterId && x.UnitId == id, ct);
        if (meter is null) return NotFound();
        if (Api.In(m.MeterType, V.MeterType, "meter_type") is { } err) return this.Error(err);
        meter.MeterType = m.MeterType; meter.SerialNo = m.SerialNo.Trim(); meter.Active = m.Active;
        meter.LastReading = m.LastReading; meter.LastReadingDate = m.LastReadingDate;
        return await this.TrySaveAsync(db, ct) ?? Ok(new { meter.Id });
    }

    // ---------- bulk import ----------

    public record ImportRow(int Row, string Code, string? Block, string UnitType, string? Floor, decimal? AreaSqft, decimal DefaultRent, string Currency, string Status);
    public record ImportResult(int Created, int Updated, List<string> Errors, bool DryRun);

    /// <summary>
    /// Bulk import units for a property from CSV or Excel (.xlsx). Columns (header row, any order):
    /// code, block, unit_type, floor, area_sqft, default_rent, currency, status. Existing codes are updated.
    /// Nothing is saved if any row has an error; dryRun=true only validates.
    /// </summary>
    [HttpPost("import/{propertyId:guid}"), Authorize(Policy = Policies.Operations), RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Import(Guid propertyId, IFormFile file, [FromQuery] bool dryRun, CancellationToken ct)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == propertyId, ct);
        if (property is null) return NotFound();

        List<Dictionary<string, string>> table;
        try
        {
            await using var s = file.OpenReadStream();
            table = file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadXlsx(s) : ReadCsv(s);
        }
        catch (Exception ex) { return this.Error($"Could not read the file: {ex.Message}"); }

        var (rows, errors) = ParseRows(table, property.DefaultCurrency);
        var dupes = rows.GroupBy(r => r.Code).Where(g => g.Count() > 1).Select(g => $"Code {g.Key} appears more than once");
        errors.AddRange(dupes);

        var existing = await db.Units.Where(u => u.PropertyId == propertyId).ToDictionaryAsync(u => u.Code, ct);
        var blocks = await db.Blocks.Where(b => b.PropertyId == propertyId).ToDictionaryAsync(b => b.Name, StringComparer.OrdinalIgnoreCase, ct);
        var letUnits = await db.Leases.Where(l => l.Status == "active" && l.Unit!.PropertyId == propertyId).Select(l => l.Unit!.Code).ToListAsync(ct);
        foreach (var r in rows)
        {
            if (r.Status == "occupied" && !letUnits.Contains(r.Code)) errors.Add($"Row {r.Row}: a unit becomes occupied when a lease is activated");
            if (letUnits.Contains(r.Code) && r.Status != "occupied") errors.Add($"Row {r.Row}: {r.Code} has an active lease; its status cannot change here");
        }

        int created = 0, updated = 0;
        if (errors.Count == 0 && !dryRun)
        {
            foreach (var r in rows)
            {
                Guid? blockId = null;
                if (!string.IsNullOrWhiteSpace(r.Block))
                {
                    if (!blocks.TryGetValue(r.Block, out var b)) { b = new Block { PropertyId = propertyId, Name = r.Block }; db.Blocks.Add(b); blocks[r.Block] = b; }
                    blockId = b.Id;
                }
                if (!existing.TryGetValue(r.Code, out var u)) { u = new Unit { PropertyId = propertyId, Code = r.Code }; db.Units.Add(u); created++; }
                else updated++;
                u.BlockId = blockId; u.UnitType = r.UnitType; u.Floor = r.Floor; u.AreaSqft = r.AreaSqft;
                u.DefaultRent = r.DefaultRent; u.DefaultCurrency = r.Currency; u.Status = r.Status;
            }
            if (await this.TrySaveAsync(db, ct) is { } fail) return fail;
        }
        else if (errors.Count == 0)
        {
            created = rows.Count(r => !existing.ContainsKey(r.Code));
            updated = rows.Count - created;
        }
        return Ok(new ImportResult(created, updated, errors, dryRun || errors.Count > 0));
    }

    public static (List<ImportRow> Rows, List<string> Errors) ParseRows(List<Dictionary<string, string>> table, string defaultCurrency)
    {
        var rows = new List<ImportRow>();
        var errors = new List<string>();
        for (var i = 0; i < table.Count; i++)
        {
            var r = table[i];
            var n = i + 2; // header is row 1
            string G(string k) => r.TryGetValue(k, out var v) ? v.Trim() : "";
            var code = G("code").ToUpperInvariant();
            if (code.Length == 0 && r.Values.All(string.IsNullOrWhiteSpace)) continue;
            var type = G("unit_type").ToLowerInvariant();
            var status = G("status").ToLowerInvariant(); if (status == "") status = "vacant";
            var cur = G("currency").ToUpperInvariant(); if (cur == "") cur = defaultCurrency;
            var err = Api.First(
                code.Length is 0 or > 20 ? "code is required (max 20)" : null,
                Api.In(type, V.UnitType, "unit_type"), Api.In(status, V.UnitStatus, "status"), Api.In(cur, V.Currency, "currency"));
            if (!TryDec(G("default_rent"), out var rent) || rent < 0) err ??= "default_rent must be a number";
            decimal? area = null;
            if (G("area_sqft") != "") { if (TryDec(G("area_sqft"), out var a)) area = a; else err ??= "area_sqft must be a number"; }
            if (err is not null) { errors.Add($"Row {n}: {err}"); continue; }
            rows.Add(new ImportRow(n, code, G("block") is { Length: > 0 } b ? b : null, type, G("floor") is { Length: > 0 } f ? f : null, area, rent, cur, status));
        }
        return (rows, errors);
    }

    static bool TryDec(string s, out decimal d) =>
        decimal.TryParse(s.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out d);

    static string Key(string h) => h.Trim().ToLowerInvariant().Replace(" ", "_");

    public static List<Dictionary<string, string>> ReadCsv(Stream s)
    {
        using var reader = new StreamReader(s);
        var lines = new List<List<string>>();
        while (reader.ReadLine() is { } line)
            if (line.Trim().Length > 0) lines.Add(SplitCsv(line));
        if (lines.Count == 0) return [];
        var header = lines[0].Select(Key).ToList();
        return lines.Skip(1).Select(cells => header.Select((h, i) => (h, v: i < cells.Count ? cells[i] : ""))
            .ToDictionary(x => x.h, x => x.v)).ToList();
    }

    static List<string> SplitCsv(string line)
    {
        var cells = new List<string>(); var cur = new System.Text.StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cur.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(c);
        }
        cells.Add(cur.ToString());
        return cells;
    }

    static List<Dictionary<string, string>> ReadXlsx(Stream s)
    {
        using var wb = new XLWorkbook(s);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed();
        if (used is null) return [];
        var header = used.FirstRow().Cells().Select(c => Key(c.GetString())).ToList();
        return used.RowsUsed().Skip(1).Select(row => header.Select((h, i) => (h, v: row.Cell(i + 1).GetFormattedString()))
            .ToDictionary(x => x.h, x => x.v)).ToList();
    }
}
