using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NavEstate.Api.Auth;
using NavEstate.Api.Data;
using NavEstate.Api.Domain;

namespace NavEstate.Api.Features;

[Route("api/landlords")]
public class LandlordsController(NavEstateDbContext db, IAuthorizationService a) : CrudController<Landlord, Guid>(db, a)
{
    protected override string WritePolicy => Policies.Operations;
    protected override IQueryable<Landlord> Ordered(IQueryable<Landlord> q) => q.OrderBy(x => x.Name);
    protected override Guid KeyOf(Landlord e) => e.Id;
    protected override void SetKey(Landlord e, Guid key) => e.Id = key;
    protected override string? Validate(Landlord e) => Api.In(e.Type, V.LandlordType, "type");

    /// <summary>Only landlords that own at least one visible property, unless super admin.</summary>
    [HttpGet]
    public override async Task<IActionResult> List(CancellationToken ct)
    {
        var q = Db.Landlords.AsNoTracking();
        if (!User.IsInRole(Roles.SuperAdmin) && !User.IsInRole(Roles.PropertyManager))
            q = q.Where(l => l.Properties.Any());
        return Ok(await q.OrderBy(x => x.Name).ToListAsync(ct));
    }
}

[ApiController, Route("api/properties"), Authorize(Policy = Policies.Staff)]
public class PropertiesController(NavEstateDbContext db) : ControllerBase
{
    public record PropertyInput(Guid LandlordId, string Code, string Name, string Type, string? Location, string? County,
        string? BulkWaterMeterNo, string DefaultCurrency);

    /// <summary>List with occupancy (row-level security limits it to the user's properties).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q, CancellationToken ct)
    {
        var query = db.Properties.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(p => p.Name.Contains(q) || p.Code.Contains(q) || (p.Location ?? "").Contains(q));
        var rows = await query.OrderBy(p => p.Name).Select(p => new
        {
            p.Id, p.Code, p.Name, p.Type, p.Location, p.County, p.DefaultCurrency, p.LandlordId,
            Landlord = p.Landlord!.Name,
            Units = p.Units.Count,
            Occupied = p.Units.Count(u => u.Status == "occupied"),
            Vacant = p.Units.Count(u => u.Status == "vacant"),
            OffMarket = p.Units.Count(u => u.Status == "off_market" || u.Status == "maintenance"),
        }).ToListAsync(ct);
        return Ok(rows.Select(r => new
        {
            r.Id, r.Code, r.Name, r.Type, r.Location, r.County, r.DefaultCurrency, r.LandlordId, r.Landlord,
            r.Units, r.Occupied, r.Vacant, r.OffMarket,
            OccupancyPercent = Occupancy(r.Occupied, r.Units, r.OffMarket)
        }));
    }

    /// <summary>Occupied ÷ lettable units (off-market and under-maintenance units are not lettable).</summary>
    public static decimal Occupancy(int occupied, int units, int notLettable)
    {
        var lettable = units - notLettable;
        return lettable <= 0 ? 0 : Math.Round(100m * occupied / lettable, 1);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var p = await db.Properties.AsNoTracking().Include(x => x.Landlord).Include(x => x.Blocks.OrderBy(b => b.Name))
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        var units = await db.Units.AsNoTracking().Where(u => u.PropertyId == id).OrderBy(u => u.Code)
            .Select(u => new
            {
                u.Id, u.Code, u.UnitType, u.Floor, u.AreaSqft, u.Status, u.DefaultRent, u.DefaultCurrency, u.BlockId,
                Block = u.Block != null ? u.Block.Name : null,
                Meters = u.Meters.Count(m => m.Active),
                Charges = u.Charges.Count,
                Tenant = db.Leases.Where(l => l.UnitId == u.Id && l.Status == "active").Select(l => l.Tenant!.FullName).FirstOrDefault()
            }).ToListAsync(ct);
        var tariffs = await db.WaterTariffs.AsNoTracking().Where(t => t.PropertyId == id).Select(t => new { t.Id, t.Name, t.EffectiveFrom }).ToListAsync(ct);
        return Ok(new
        {
            p.Id, p.Code, p.Name, p.Type, p.Location, p.County, p.BulkWaterMeterNo, p.DefaultCurrency, p.LandlordId,
            Landlord = p.Landlord?.Name,
            Blocks = p.Blocks.Select(b => new { b.Id, b.Name }),
            Units = units,
            WaterTariffs = tariffs,
            StatusCounts = units.GroupBy(u => u.Status).ToDictionary(g => g.Key, g => g.Count())
        });
    }

    string? Validate(PropertyInput i) => Api.First(
        string.IsNullOrWhiteSpace(i.Code) || i.Code.Length > 20 ? "Code is required (max 20 characters; it becomes the BC PROPERTY dimension value)" : null,
        string.IsNullOrWhiteSpace(i.Name) ? "Name is required" : null,
        Api.In(i.Type, V.PropertyType, "type"), Api.In(i.DefaultCurrency, V.Currency, "default_currency"));

    [HttpPost, Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> Create(PropertyInput input, CancellationToken ct)
    {
        if (Validate(input) is { } err) return this.Error(err);
        if (!await db.Landlords.AnyAsync(l => l.Id == input.LandlordId, ct)) return this.Error("Landlord not found");
        var p = new Property();
        Apply(p, input);
        db.Properties.Add(p);
        // A property manager who creates a property is assigned to it, so row-level security lets them see it.
        if (!User.IsInRole(Roles.SuperAdmin))
            db.UserPropertyAssignments.Add(new UserPropertyAssignment { UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value, PropertyId = p.Id });
        return await this.TrySaveAsync(db, ct) ?? Ok(new { p.Id });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> Update(Guid id, PropertyInput input, CancellationToken ct)
    {
        if (Validate(input) is { } err) return this.Error(err);
        var p = await db.Properties.FindAsync([id], ct);
        if (p is null) return NotFound();
        if (p.Code != input.Code.Trim().ToUpperInvariant() && await db.Units.AnyAsync(u => u.PropertyId == id && u.Status == "occupied", ct))
            return this.Error("The code cannot change once units are let: it is the BC PROPERTY dimension on posted documents");
        Apply(p, input);
        return await this.TrySaveAsync(db, ct) ?? Ok(new { p.Id });
    }

    static void Apply(Property p, PropertyInput i)
    {
        p.LandlordId = i.LandlordId; p.Code = i.Code.Trim().ToUpperInvariant(); p.Name = i.Name.Trim(); p.Type = i.Type;
        p.Location = i.Location; p.County = i.County; p.BulkWaterMeterNo = i.BulkWaterMeterNo; p.DefaultCurrency = i.DefaultCurrency;
    }

    public record BlockInput(string Name);

    [HttpPost("{id:guid}/blocks"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> AddBlock(Guid id, BlockInput input, CancellationToken ct)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == id, ct)) return NotFound();
        if (string.IsNullOrWhiteSpace(input.Name)) return this.Error("Name is required");
        var b = new Block { PropertyId = id, Name = input.Name.Trim() };
        db.Blocks.Add(b);
        return await this.TrySaveAsync(db, ct) ?? Ok(new { b.Id, b.Name });
    }

    [HttpDelete("{id:guid}/blocks/{blockId:guid}"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> DeleteBlock(Guid id, Guid blockId, CancellationToken ct)
    {
        var b = await db.Blocks.FirstOrDefaultAsync(x => x.Id == blockId && x.PropertyId == id, ct);
        if (b is null) return NotFound();
        if (await db.Units.AnyAsync(u => u.BlockId == blockId, ct)) return Conflict(new { error = "Move the block's units first" });
        db.Blocks.Remove(b);
        return await this.TrySaveAsync(db, ct) ?? NoContent();
    }
}
