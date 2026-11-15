using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NavEstate.Api.Auth;
using NavEstate.Api.Bc;
using NavEstate.Api.Common;
using NavEstate.Api.Data;
using NavEstate.Api.Domain;

namespace NavEstate.Api.Features;

[ApiController, Route("api/tenants"), Authorize(Policy = Policies.Staff)]
public class TenantsController(NavEstateDbContext db, NumberSeriesService numbers, BcSyncQueue bcQueue,
    IFileStore files, IOptions<FileStoreOptions> fileOptions) : ControllerBase
{
    public record TenantInput(
        string Type, string FullName, string? IdOrPassportNo, string? KraPin, string Phone, string? AltPhone, string? Email,
        string? NextOfKinName, string? NextOfKinPhone, string? CompanyRegNo, string? ContactPerson,
        bool Blacklisted, string? BlacklistReason);

    /// <summary>
    /// Tenants the user may see: those with no lease yet (new applicants) or with a lease on a unit the
    /// user can see (units are filtered by row-level security).
    /// </summary>
    IQueryable<Tenant> Visible() => User.IsInRole(Roles.SuperAdmin)
        ? db.Tenants
        : db.Tenants.Where(t => !t.Leases.Any() || t.Leases.Any(l => db.Units.Any(u => u.Id == l.UnitId)));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] string? type, [FromQuery] bool? blacklisted,
        [FromQuery] string? bc, [FromQuery] Guid? propertyId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = Visible().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(t => t.FullName.Contains(s) || t.TenantNo.Contains(s) || t.Phone.Contains(s)
                                     || (t.IdOrPassportNo ?? "").Contains(s) || (t.Email ?? "").Contains(s)
                                     || t.Leases.Any(l => l.Status == "active" && l.Unit!.Code.Contains(s)));
        }
        if (type is not null) query = query.Where(t => t.Type == type);
        if (blacklisted is not null) query = query.Where(t => t.Blacklisted == blacklisted);
        if (bc == "synced") query = query.Where(t => t.BcSyncedAt != null);
        if (bc == "not_synced") query = query.Where(t => t.BcSyncedAt == null);
        if (propertyId is not null) query = query.Where(t => t.Leases.Any(l => l.Status == "active" && l.Unit!.PropertyId == propertyId));

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(t => t.TenantNo).Skip((page - 1) * pageSize).Take(pageSize).Select(t => new
        {
            t.Id, t.TenantNo, t.Type, t.FullName, t.Phone, t.Email, t.Blacklisted, t.BcSyncedAt,
            Unit = t.Leases.Where(l => l.Status == "active").Select(l => l.Unit!.Code).FirstOrDefault(),
            Property = t.Leases.Where(l => l.Status == "active").Select(l => l.Unit!.Property!.Name).FirstOrDefault(),
            BcStatus = db.BcSyncQueue.Where(x => x.EntityType == "tenant" && x.EntityId == t.Id).OrderByDescending(x => x.CreatedAt).Select(x => x.Status).FirstOrDefault()
        }).ToListAsync(ct);
        return Ok(new { total, page, pageSize, rows });
    }

    /// <summary>Tenant 360 header and the Leases tab.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var t = await Visible().AsNoTracking().Include(x => x.Documents).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        var leases = await db.Leases.AsNoTracking().Where(l => l.TenantId == id).OrderByDescending(l => l.StartDate).Select(l => new
        {
            l.Id, l.LeaseNo, l.Status, l.StartDate, l.EndDate, l.CurrencyCode, l.BillingDay, l.Frequency, l.DepositAmount,
            UnitId = l.UnitId, Unit = l.Unit!.Code, Property = l.Unit.Property!.Name, PropertyCode = l.Unit.Property.Code,
            MonthlyRent = l.Charges.Where(c => c.Active && (c.ChargeCode == "RENT" || c.ChargeCode == "RENTCOM")).Sum(c => c.Amount),
            Charges = l.Charges.Where(c => c.Active).Select(c => new { c.ChargeCode, c.Description, c.Amount, c.CurrencyCode, c.Frequency })
        }).ToListAsync(ct);
        var sync = await db.BcSyncQueue.AsNoTracking().Where(x => x.EntityType == "tenant" && x.EntityId == id)
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Status, x.Attempts, x.LastError, x.ProcessedAt }).FirstOrDefaultAsync(ct);
        return Ok(new
        {
            tenant = t,
            documents = t.Documents.OrderBy(d => d.DocType).Select(d => new
            {
                d.Id, d.DocType, d.FileName, d.ExpiryDate, d.CreatedAt,
                Expired = d.ExpiryDate != null && d.ExpiryDate < DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3))
            }),
            leases,
            bcSync = sync
        });
    }

    string? Validate(TenantInput i) => Api.First(
        Api.In(i.Type, V.TenantType, "type"),
        string.IsNullOrWhiteSpace(i.FullName) ? "Name is required" : null,
        Phone.Normalize(i.Phone) is null ? "A valid phone number is required, e.g. 0712 345 678" : null,
        i.AltPhone is { Length: > 0 } && Phone.Normalize(i.AltPhone) is null ? "Alternative phone is not a valid number" : null,
        i.Type == "individual" && string.IsNullOrWhiteSpace(i.IdOrPassportNo) ? "ID or passport number is required" : null,
        i.Type == "company" && string.IsNullOrWhiteSpace(i.CompanyRegNo) ? "Company registration number is required" : null,
        i.KraPin is { Length: > 0 } pin && !System.Text.RegularExpressions.Regex.IsMatch(pin.Trim().ToUpperInvariant(), "^[AP][0-9]{9}[A-Z]$") ? "KRA PIN must look like A123456789B" : null,
        i.Email is { Length: > 0 } e && !e.Contains('@') ? "Email is not valid" : null,
        i.Blacklisted && string.IsNullOrWhiteSpace(i.BlacklistReason) ? "Give a reason for blacklisting" : null);

    static void Apply(Tenant t, TenantInput i)
    {
        t.Type = i.Type; t.FullName = i.FullName.Trim(); t.IdOrPassportNo = i.IdOrPassportNo?.Trim();
        t.KraPin = string.IsNullOrWhiteSpace(i.KraPin) ? null : i.KraPin.Trim().ToUpperInvariant();
        t.Phone = Phone.Normalize(i.Phone)!; t.AltPhone = Phone.Normalize(i.AltPhone); t.Email = string.IsNullOrWhiteSpace(i.Email) ? null : i.Email.Trim();
        t.NextOfKinName = i.NextOfKinName; t.NextOfKinPhone = i.NextOfKinPhone; t.CompanyRegNo = i.CompanyRegNo; t.ContactPerson = i.ContactPerson;
        t.Blacklisted = i.Blacklisted; t.BlacklistReason = i.Blacklisted ? i.BlacklistReason : null;
    }

    /// <summary>Creates the tenant, gives it the next T0nnnn number and queues UpsertTenant, all in one transaction.</summary>
    [HttpPost, Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> Create(TenantInput input, CancellationToken ct)
    {
        if (Validate(input) is { } err) return this.Error(err);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var t = new Tenant { TenantNo = await numbers.NextTenantNoAsync(ct) };
        Apply(t, input);
        db.Tenants.Add(t);
        await bcQueue.EnqueueUpsertTenantAsync(t, ct);
        if (await this.TrySaveAsync(db, ct) is { } fail) return fail;
        await tx.CommitAsync(ct);
        return Ok(new { t.Id, t.TenantNo });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> Update(Guid id, TenantInput input, CancellationToken ct)
    {
        if (Validate(input) is { } err) return this.Error(err);
        var t = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        Apply(t, input);
        var bcRelevant = db.Entry(t).Properties.Any(p => p.IsModified && p.Metadata.Name is nameof(Tenant.FullName) or nameof(Tenant.Phone) or nameof(Tenant.Email) or nameof(Tenant.KraPin));
        if (bcRelevant || t.BcSyncedAt is null) await bcQueue.EnqueueUpsertTenantAsync(t, ct);
        return await this.TrySaveAsync(db, ct) ?? Ok(new { t.Id, t.TenantNo });
    }

    /// <summary>Re-sends the tenant to BC (UpsertTenant is idempotent).</summary>
    [HttpPost("{id:guid}/sync"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> Resync(Guid id, CancellationToken ct)
    {
        var t = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        var item = await bcQueue.EnqueueUpsertTenantAsync(t, ct);
        await db.SaveChangesAsync(ct);
        return Ok(new { item.Id, item.Status });
    }

    // ---------- documents ----------

    [HttpPost("{id:guid}/documents"), Authorize(Policy = Policies.Operations), RequestSizeLimit(12_000_000)]
    public async Task<IActionResult> Upload(Guid id, [FromForm] string docType, [FromForm] DateOnly? expiryDate, IFormFile file, CancellationToken ct)
    {
        if (!await Visible().AnyAsync(t => t.Id == id, ct)) return NotFound();
        if (Api.In(docType, V.DocType, "doc_type") is { } err) return this.Error(err);
        var o = fileOptions.Value;
        if (file.Length == 0 || file.Length > o.MaxBytes) return this.Error($"File must be between 1 byte and {o.MaxBytes / 1_000_000} MB");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!o.AllowedExtensions.Contains(ext)) return this.Error($"Allowed file types: {string.Join(", ", o.AllowedExtensions)}");

        await using var s = file.OpenReadStream();
        var path = await files.SaveAsync($"tenants/{id:N}", file.FileName, s, ct);
        var d = new TenantDocument { TenantId = id, DocType = docType, FilePath = path, FileName = Path.GetFileName(file.FileName), ExpiryDate = expiryDate };
        db.TenantDocuments.Add(d);
        await db.SaveChangesAsync(ct);
        return Ok(new { d.Id, d.DocType, d.FileName, d.ExpiryDate });
    }

    [HttpGet("{id:guid}/documents/{docId:guid}")]
    public async Task<IActionResult> Download(Guid id, Guid docId, CancellationToken ct)
    {
        if (!await Visible().AnyAsync(t => t.Id == id, ct)) return NotFound();
        var d = await db.TenantDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == docId && x.TenantId == id, ct);
        var stream = d is null ? null : files.Open(d.FilePath);
        if (stream is null) return NotFound();
        new FileExtensionContentTypeProvider().TryGetContentType(d!.FileName, out var type);
        return File(stream, type ?? "application/octet-stream", d.FileName);
    }

    [HttpDelete("{id:guid}/documents/{docId:guid}"), Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> DeleteDocument(Guid id, Guid docId, CancellationToken ct)
    {
        if (!await Visible().AnyAsync(t => t.Id == id, ct)) return NotFound();
        var d = await db.TenantDocuments.FirstOrDefaultAsync(x => x.Id == docId && x.TenantId == id, ct);
        if (d is null) return NotFound();
        db.TenantDocuments.Remove(d);
        await db.SaveChangesAsync(ct);
        files.Delete(d.FilePath);
        return NoContent();
    }

    // ---------- tenant 360 tabs (tables exist now; they fill as later stages post bills, payments, ...) ----------

    [HttpGet("{id:guid}/bills")]
    public async Task<IActionResult> Bills(Guid id, CancellationToken ct) => await Tab(id, db.Bills.Where(b => b.TenantId == id)
        .OrderByDescending(b => b.BillDate).Select(b => new { b.Id, b.BillNo, b.BillType, b.Period, b.BillDate, b.DueDate, b.CurrencyCode, b.TotalAmount, b.Balance, b.Status, b.BcDocumentNo }), ct);

    [HttpGet("{id:guid}/payments")]
    public async Task<IActionResult> Payments(Guid id, CancellationToken ct) => await Tab(id, db.Payments.Where(p => p.TenantId == id)
        .OrderByDescending(p => p.PaymentDate).Select(p => new { p.Id, p.ReceiptNo, p.Channel, p.Reference, p.PaymentDate, p.CurrencyCode, p.Amount, p.Status, p.BcDocumentNo }), ct);

    [HttpGet("{id:guid}/deposits")]
    public async Task<IActionResult> Deposits(Guid id, CancellationToken ct) => await Tab(id, db.Deposits.Where(d => d.TenantId == id)
        .Select(d => new { d.Id, d.DepositType, d.CurrencyCode, d.AmountHeld, d.Status, d.LeaseId }), ct);

    [HttpGet("{id:guid}/maintenance")]
    public async Task<IActionResult> Maintenance(Guid id, CancellationToken ct) => await Tab(id, db.MaintenanceRequests.Where(m => m.TenantId == id)
        .OrderByDescending(m => m.CreatedAt).Select(m => new { m.Id, m.RequestNo, m.Category, m.Priority, m.Status, m.Description, m.CreatedAt }), ct);

    [HttpGet("{id:guid}/notices")]
    public async Task<IActionResult> Notices(Guid id, CancellationToken ct) => await Tab(id, db.Notices.Where(n => n.TenantId == id)
        .OrderByDescending(n => n.CreatedAt).Select(n => new { n.Id, n.Channel, n.Subject, n.Body, n.Status, n.SentAt }), ct);

    [HttpGet("{id:guid}/audit")]
    public async Task<IActionResult> Audit(Guid id, CancellationToken ct)
    {
        var key = id.ToString();
        return await Tab(id, db.AuditLog.Where(a => a.RecordId == key && a.TableName == "tenancy.tenants")
            .OrderByDescending(a => a.CreatedAt).Take(200)
            .Select(a => new { a.Id, a.Action, a.OldValues, a.NewValues, a.CreatedAt, User = db.Users.Where(u => u.Id == a.UserId).Select(u => u.FullName).FirstOrDefault() ?? a.UserId }), ct);
    }

    async Task<IActionResult> Tab<T>(Guid id, IQueryable<T> q, CancellationToken ct)
    {
        if (!await Visible().AnyAsync(t => t.Id == id, ct)) return NotFound();
        return Ok(await q.ToListAsync(ct));
    }
}
