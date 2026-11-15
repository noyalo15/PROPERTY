using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NavEstate.Api.Common;
using NavEstate.Api.Data;
using NavEstate.Api.Domain;

namespace NavEstate.Api.Bc;

/// <summary>Writes bc_sync_queue rows. Call inside the same SaveChanges as the business change.</summary>
public class BcSyncQueue(NavEstateDbContext db, IOptions<BcOptions> options, TimeProvider clock)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyList<BcParam> p) => JsonSerializer.Serialize(p, Json);
    public static IReadOnlyList<BcParam> Deserialize(string payload) => JsonSerializer.Deserialize<List<BcParam>>(payload, Json) ?? [];

    /// <summary>
    /// Queues UpsertTenant with the tenant's current details. A tenant must sync before its bills,
    /// so later rows point at this one through depends_on_id. A still-pending row for the same
    /// tenant is refreshed instead of adding a second one.
    /// </summary>
    public async Task<BcSyncQueueItem> EnqueueUpsertTenantAsync(Tenant t, CancellationToken ct = default)
    {
        var o = options.Value;
        var lease = await db.Leases.AsNoTracking()
            .Where(l => l.TenantId == t.Id && l.Status == "active")
            .OrderByDescending(l => l.StartDate)
            .Select(l => new { l.CurrencyCode, UnitCode = l.Unit!.Code, PropertyCode = l.Unit.Property!.Code })
            .FirstOrDefaultAsync(ct);

        var p = new List<BcParam>
        {
            new("tenantNo", t.TenantNo),
            new("name", BcFormat.Clean(t.FullName, 100)),
            new("phone", BcFormat.Clean(t.Phone, 30)),
            new("email", BcFormat.Clean(t.Email, 80)),
            new("kRAPIN", BcFormat.Clean(t.KraPin, 20)),
            new("currencyCode", BcFormat.Currency(lease?.CurrencyCode ?? "KES")),
            new("custPostingGroup", o.CustPostingGroup),
            new("vATBusPostingGroup", o.VatBusPostingGroup),
            new("propertyCode", lease?.PropertyCode ?? ""),
            new("unitCode", lease?.UnitCode ?? ""),
        };

        var pending = db.BcSyncQueue.Local.FirstOrDefault(q => q.EntityType == "tenant" && q.EntityId == t.Id && q.Status == "pending")
            ?? await db.BcSyncQueue.FirstOrDefaultAsync(q => q.EntityType == "tenant" && q.EntityId == t.Id && q.Status == "pending", ct);
        if (pending is not null)
        {
            pending.Payload = Serialize(p);
            pending.ExternalRef = t.TenantNo;
            return pending;
        }

        var item = new BcSyncQueueItem
        {
            EntityType = "tenant", EntityId = t.Id, FunctionName = "UpsertTenant", ExternalRef = t.TenantNo,
            Payload = Serialize(p), Status = "pending", NextAttemptAt = null
        };
        db.BcSyncQueue.Add(item);
        return item;
    }
}

/// <summary>
/// Drains bc_sync_queue into BC, one row at a time (BC posts synchronously; one worker per company
/// avoids table locks). Retries after 1, 5, 15 and 60 minutes, 5 attempts in total.
/// </summary>
public class BcSyncWorker(NavEstateDbContext db, IBcClient bc, IOptions<BcOptions> options, TimeProvider clock, ILogger<BcSyncWorker> log)
{
    public const int MaxAttempts = 5;
    static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60)];

    /// <summary>When the next try happens after <paramref name="attempts"/> failed attempts; null = give up.</summary>
    public static TimeSpan? DelayAfter(int attempts) => attempts >= MaxAttempts ? null : Backoff[Math.Min(attempts - 1, Backoff.Length - 1)];

    public record RunSummary(int Processed, int Succeeded, int Failed, int Skipped);

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    public async Task<RunSummary> ProcessDueAsync(CancellationToken ct = default)
    {
        var now = clock.Now();

        // A row left in "processing" by a crashed run is picked up again; idempotency on externalRef makes that safe.
        var stuckBefore = now.AddMinutes(-10);
        await db.BcSyncQueue.Where(q => q.Status == "processing" && q.UpdatedAt < stuckBefore)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, "pending"), ct);

        var due = await db.BcSyncQueue
            .Where(q => (q.Status == "pending" || q.Status == "failed") && q.Attempts < MaxAttempts
                        && (q.NextAttemptAt == null || q.NextAttemptAt <= now))
            .OrderBy(q => q.CreatedAt).ThenBy(q => q.Id)
            .Take(options.Value.BatchSize)
            .ToListAsync(ct);

        int ok = 0, failed = 0, skipped = 0;
        foreach (var item in due)
        {
            if (item.DependsOnId is { } dep)
            {
                var depStatus = await db.BcSyncQueue.Where(q => q.Id == dep).Select(q => q.Status).FirstOrDefaultAsync(ct);
                if (depStatus != "success") { skipped++; continue; }
            }

            item.Status = "processing";
            await db.SaveChangesAsync(ct);

            var sw = Stopwatch.StartNew();
            BcResult result;
            try { result = await bc.CallAsync(item.FunctionName, BcSyncQueue.Deserialize(item.Payload), ct); }
            catch (Exception ex) { result = BcResult.Fail(ex.Message); }
            log.LogInformation("BC {Function} {Ref}: {Ok} in {Ms} ms", item.FunctionName, item.ExternalRef, result.Success, sw.ElapsedMilliseconds);

            item.Attempts++;
            item.ProcessedAt = clock.Now();
            if (result.Success)
            {
                item.Status = "success";
                item.BcDocumentNo = result.DocumentNo;
                item.LastError = null;
                item.NextAttemptAt = null;
                await WriteBackAsync(item, ct);
                ok++;
            }
            else
            {
                item.Status = "failed";
                item.LastError = Truncate(result.Error, 1000);
                var delay = DelayAfter(item.Attempts);
                item.NextAttemptAt = delay is null ? null : clock.Now().Add(delay.Value);
                failed++;
            }
            await db.SaveChangesAsync(ct);
        }
        return new RunSummary(due.Count - skipped, ok, failed, skipped);
    }

    /// <summary>Saves the BC result on the source record.</summary>
    async Task WriteBackAsync(BcSyncQueueItem item, CancellationToken ct)
    {
        switch (item.EntityType)
        {
            case "tenant":
                var t = await db.Tenants.FindAsync([item.EntityId], ct);
                if (t is not null) t.BcSyncedAt = clock.Now();
                break;
            case "bill":
                var b = await db.Bills.FindAsync([item.EntityId], ct);
                if (b is not null) { b.BcDocumentNo = item.BcDocumentNo; b.BcSyncStatus = "success"; if (b.Status == "approved") b.Status = "posted"; }
                break;
            case "credit_note":
                var c = await db.CreditNotes.FindAsync([item.EntityId], ct);
                if (c is not null) { c.BcDocumentNo = item.BcDocumentNo; c.Status = "posted"; }
                break;
            case "payment":
                var p = await db.Payments.FindAsync([item.EntityId], ct);
                if (p is not null) { p.BcDocumentNo = item.BcDocumentNo; p.BcSyncStatus = "success"; p.Status = "posted"; }
                break;
            case "deposit":
                var d = await db.DepositMovements.FindAsync([item.EntityId], ct);
                if (d is not null) d.BcDocumentNo = item.BcDocumentNo;
                break;
        }
    }

    /// <summary>Retry button: give failed rows a fresh set of attempts.</summary>
    public async Task<int> RetryFailedAsync(Guid? id, CancellationToken ct = default)
    {
        var q = db.BcSyncQueue.Where(x => x.Status == "failed");
        if (id is not null) q = q.Where(x => x.Id == id);
        var rows = await q.ToListAsync(ct);
        foreach (var r in rows) { r.Status = "pending"; r.Attempts = 0; r.NextAttemptAt = null; } // null = due now
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}

/// <summary>Ping every 5 minutes (drives the "BC 13 connected" pill) and the daily USD rate.</summary>
public class BcHealthJobs(NavEstateDbContext db, IBcClient bc, TimeProvider clock)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task<BcPingLog> PingAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        BcResult r;
        try { r = await bc.CallAsync("Ping", [], ct); }
        catch (Exception ex) { r = BcResult.Fail(ex.Message); }
        var msg = r.Success ? r.DocumentNo : r.Error;
        var row = new BcPingLog { At = clock.Now(), Ok = r.Success, Message = msg is { Length: > 500 } ? msg[..500] : msg, DurationMs = (int)sw.ElapsedMilliseconds };
        db.BcPingLog.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task<decimal?> RefreshUsdRateAsync(CancellationToken ct = default)
    {
        var today = clock.Today();
        var r = await bc.CallAsync("GetExchangeRate", [new("currencyCode", "USD"), new("rateDate", BcFormat.Date(today))], ct);
        if (!r.Success || !decimal.TryParse(r.DocumentNo, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate) || rate <= 0) return null;

        var row = await db.ExchangeRates.FirstOrDefaultAsync(x => x.CurrencyCode == "USD" && x.RateDate == today, ct);
        if (row is null) db.ExchangeRates.Add(new ExchangeRate { CurrencyCode = "USD", RateDate = today, KesPerUnit = rate, Source = "BC" });
        else { row.KesPerUnit = rate; row.Source = "BC"; }
        await db.SaveChangesAsync(ct);
        return rate;
    }
}
