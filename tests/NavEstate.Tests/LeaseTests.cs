using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NavEstate.Api.Bc;
using NavEstate.Api.Data;
using NavEstate.Api.Features;
using Xunit;

namespace NavEstate.Tests;

[Collection("api")]
public class LeaseTests(ApiFactory f)
{
    static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
    static int phoneSeq = 300;

    static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        r.IsSuccessStatusCode.Should().BeTrue($"{(int)r.StatusCode}: {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    static async Task<string> Fails(HttpResponseMessage r, HttpStatusCode code)
    {
        var text = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(code, text);
        return text;
    }

    async Task<T> WithDb<T>(Func<NavEstateDbContext, Task<T>> fn)
    {
        using var scope = f.Services.CreateScope();
        return await fn(scope.ServiceProvider.GetRequiredService<NavEstateDbContext>());
    }

    async Task Advance(DateOnly day)
    {
        using var scope = f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RentScheduleService>().AdvanceAsync(day, null, default);
    }

    static async Task<Guid> NewTenant(HttpClient c, bool blacklisted = false)
    {
        var n = Interlocked.Increment(ref phoneSeq);
        var t = await Json(await c.PostAsJsonAsync("/api/tenants", new
        {
            type = "individual", fullName = $"Lease Test {n}", idOrPassportNo = $"3{n:D7}", phone = $"0798 000 {n:D3}",
            blacklisted, blacklistReason = blacklisted ? "Arrears" : null
        }));
        return t.GetProperty("id").GetGuid();
    }

    /// <summary>The first vacant Riverside unit (the property manager's), with its default charges.</summary>
    static async Task<JsonElement> VacantUnit(HttpClient c, int skip = 0)
    {
        var units = await Json(await c.GetAsync("/api/leases/vacant-units?q=Riverside"));
        return units.EnumerateArray().Where(u => u.GetProperty("status").GetString() == "vacant").Skip(skip).First();
    }

    static object Input(Guid tenantId, JsonElement unit, DateOnly start, DateOnly? end, object? escalation, bool overrideBlacklist = false)
    {
        var charges = new List<object> { new { chargeCode = "RENT", description = "Rent", amount = unit.GetProperty("defaultRent").GetDecimal(), frequency = "monthly" } };
        charges.AddRange(unit.GetProperty("charges").EnumerateArray().Select(c => (object)new
        {
            chargeCode = c.GetProperty("chargeCode").GetString(), description = c.GetProperty("chargeCode").GetString(),
            amount = c.GetProperty("amount").GetDecimal(), frequency = c.GetProperty("frequency").GetString()
        }));
        return new
        {
            tenantId, unitId = unit.GetProperty("id").GetGuid(), startDate = start, endDate = end, currencyCode = unit.GetProperty("defaultCurrency").GetString(),
            billingDay = 1, frequency = "monthly", charges, depositAmount = unit.GetProperty("defaultRent").GetDecimal(), waterDepositAmount = 2500m,
            graceDays = 5, penaltyRuleId = (Guid?)null, vatApplicable = false, whtAgent = false, escalation, overrideBlacklist
        };
    }

    static readonly object FivePercent = new { type = "fixed_percent", value = 5m, everyMonths = 12, compounding = true, noticeDays = 90, requiresApproval = true };

    [Fact]
    public async Task Wizard_activation_occupies_the_unit_creates_deposits_and_requeues_the_tenant()
    {
        var pm = await f.LoginAsync("pm");
        var tenantId = await NewTenant(pm);
        var unit = await VacantUnit(pm);
        var unitId = unit.GetProperty("id").GetGuid();

        var created = await Json(await pm.PostAsJsonAsync("/api/leases?activate=true", Input(tenantId, unit, Today, Today.AddYears(3).AddDays(-1), FivePercent)));
        created.GetProperty("leaseNo").GetString().Should().MatchRegex("^LSE-\\d{4}$");
        created.GetProperty("status").GetString().Should().Be("active");
        var id = created.GetProperty("id").GetGuid();

        var detail = await Json(await pm.GetAsync($"/api/leases/{id}"));
        detail.GetProperty("unit").GetProperty("status").GetString().Should().Be("occupied");
        var deposits = detail.GetProperty("deposits").EnumerateArray().ToList();
        deposits.Select(d => d.GetProperty("depositType").GetString()).Should().BeEquivalentTo(["rent", "water"]);
        deposits.Should().OnlyContain(d => d.GetProperty("status").GetString() == "held" && d.GetProperty("amountHeld").GetDecimal() == 0, "deposits are awaiting receipt");
        var schedule = detail.GetProperty("schedule").EnumerateArray().ToList();
        schedule.Select(r => r.GetProperty("status").GetString()).Should().Equal("current", "scheduled", "scheduled");
        detail.GetProperty("charges").EnumerateArray().First().GetProperty("bcAccount").GetString().Should().NotBeNullOrEmpty();

        var queued = await WithDb(db => db.BcSyncQueue.Where(q => q.EntityId == tenantId && q.Status == "pending").SingleAsync());
        BcSyncQueue.Deserialize(queued.Payload).Single(p => p.Name == "unitCode").Value.Should().Be(unit.GetProperty("code").GetString());

        // The unit is no longer offered and a second lease on it is refused.
        var other = await NewTenant(pm);
        (await Fails(await pm.PostAsJsonAsync("/api/leases", Input(other, unit, Today, null, null)), HttpStatusCode.BadRequest)).Should().Contain("only vacant units");
        (await Json(await pm.GetAsync("/api/leases/vacant-units?q=Riverside"))).EnumerateArray().Should().NotContain(u => u.GetProperty("id").GetGuid() == unitId);
    }

    [Fact]
    public async Task Two_drafts_for_one_unit_cannot_both_be_activated()
    {
        var pm = await f.LoginAsync("pm");
        var unit = await VacantUnit(pm, 1);
        var a = await Json(await pm.PostAsJsonAsync("/api/leases", Input(await NewTenant(pm), unit, Today, null, null)));
        var b = await Json(await pm.PostAsJsonAsync("/api/leases", Input(await NewTenant(pm), unit, Today, null, null)));
        a.GetProperty("status").GetString().Should().Be("draft");

        await Json(await pm.PostAsJsonAsync($"/api/leases/{a.GetProperty("id")}/activate", new { }));
        (await Fails(await pm.PostAsJsonAsync($"/api/leases/{b.GetProperty("id")}/activate", new { }), HttpStatusCode.Conflict)).Should().Contain("no longer vacant");
        (await pm.DeleteAsync($"/api/leases/{b.GetProperty("id")}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Fails(await pm.DeleteAsync($"/api/leases/{a.GetProperty("id")}"), HttpStatusCode.BadRequest)).Should().Contain("Only draft");
    }

    [Fact]
    public async Task Blacklisted_tenants_need_an_explicit_override()
    {
        var pm = await f.LoginAsync("pm");
        var tenant = await NewTenant(pm, blacklisted: true);
        var unit = await VacantUnit(pm, 2);
        (await Fails(await pm.PostAsJsonAsync("/api/leases", Input(tenant, unit, Today, null, null)), HttpStatusCode.BadRequest)).Should().Contain("blacklisted");
        await Json(await pm.PostAsJsonAsync("/api/leases", Input(tenant, unit, Today, null, null, overrideBlacklist: true)));
    }

    [Fact]
    public async Task Escalation_needs_accountant_approval_sends_the_notice_and_applies_on_its_date()
    {
        var pm = await f.LoginAsync("pm");
        var tenantId = await NewTenant(pm);
        var unit = await VacantUnit(pm, 3);
        var rent = unit.GetProperty("defaultRent").GetDecimal();
        // Started 11 months ago: the first 5% escalation is a month away, inside the 90-day notice window.
        var start = Today.AddMonths(-11);
        var id = (await Json(await pm.PostAsJsonAsync("/api/leases?activate=true", Input(tenantId, unit, start, start.AddYears(3).AddDays(-1), FivePercent)))).GetProperty("id").GetGuid();

        var detail = await Json(await pm.GetAsync($"/api/leases/{id}"));
        var row = detail.GetProperty("schedule")[1];
        row.GetProperty("status").GetString().Should().Be("pending_approval");
        row.GetProperty("monthlyRent").GetDecimal().Should().Be(Math.Round(rent * 1.05m, 2, MidpointRounding.AwayFromZero));
        var rowId = row.GetProperty("id").GetGuid();
        (await Json(await pm.GetAsync("/api/leases?escalationsDue=true&pageSize=200"))).GetProperty("rows").EnumerateArray()
            .Should().Contain(l => l.GetProperty("id").GetGuid() == id);

        // Unapproved escalations do not take effect.
        await Advance(start.AddMonths(12));
        (await WithDb(db => db.LeaseCharges.SingleAsync(c => c.LeaseId == id && c.ChargeCode == "RENT"))).Amount.Should().Be(rent);

        // Property managers cannot approve; the accountant can.
        (await pm.PostAsJsonAsync($"/api/leases/{id}/schedule/{rowId}/approve", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var acc = await f.LoginAsync("accounts");
        var approved = await Json(await acc.PostAsJsonAsync($"/api/leases/{id}/schedule/{rowId}/approve", new { }));
        approved.GetProperty("status").GetString().Should().Be("approved");

        var phone = await WithDb(db => db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Phone).SingleAsync());
        f.Sms.Last[phone].Should().Contain($"KES {rent:#,##0.00}").And.Contain($"KES {Math.Round(rent * 1.05m, 2, MidpointRounding.AwayFromZero):#,##0.00}");
        (await WithDb(db => db.Notices.CountAsync(n => n.TenantId == tenantId && n.Status == "sent"))).Should().Be(1);

        // On the effective date the daily job makes it current and the RENT charge follows.
        await Advance(start.AddMonths(12));
        detail = await Json(await pm.GetAsync($"/api/leases/{id}"));
        detail.GetProperty("schedule").EnumerateArray().Select(r => r.GetProperty("status").GetString()).Should().Equal("completed", "current", "scheduled");
        (await WithDb(db => db.LeaseCharges.SingleAsync(c => c.LeaseId == id && c.ChargeCode == "RENT"))).Amount.Should().Be(Math.Round(rent * 1.05m, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public async Task Renewal_extends_the_schedule_and_termination_frees_the_unit()
    {
        var pm = await f.LoginAsync("pm");
        var unit = await VacantUnit(pm, 4);
        var end = Today.AddYears(2).AddDays(-1);
        var id = (await Json(await pm.PostAsJsonAsync("/api/leases?activate=true", Input(await NewTenant(pm), unit, Today, end, FivePercent)))).GetProperty("id").GetGuid();

        (await Fails(await pm.PostAsJsonAsync($"/api/leases/{id}/renew", new { newEndDate = end }), HttpStatusCode.BadRequest)).Should().Contain("must be after");
        var renewed = await Json(await pm.PostAsJsonAsync($"/api/leases/{id}/renew", new { newEndDate = end.AddYears(2) }));
        renewed.GetProperty("added").GetInt32().Should().Be(2);
        var schedule = (await Json(await pm.GetAsync($"/api/leases/{id}"))).GetProperty("schedule").EnumerateArray().ToList();
        schedule.Should().HaveCount(4);
        var rent = unit.GetProperty("defaultRent").GetDecimal();
        schedule[3].GetProperty("monthlyRent").GetDecimal().Should().Be(Math.Round(Math.Round(Math.Round(rent * 1.05m, 2, MidpointRounding.AwayFromZero) * 1.05m, 2, MidpointRounding.AwayFromZero) * 1.05m, 2, MidpointRounding.AwayFromZero));
        schedule[1].GetProperty("effectiveTo").GetString().Should().Be(Today.AddYears(2).AddDays(-1).ToString("yyyy-MM-dd"));
        schedule[3].GetProperty("effectiveTo").GetString().Should().Be(end.AddYears(2).ToString("yyyy-MM-dd"));

        // Active leases: rent changes only through the schedule; other terms can change.
        var input = Input(Guid.Empty, unit, Today, end.AddYears(2), null);
        var detail = await Json(await pm.GetAsync($"/api/leases/{id}"));
        var tenantId = detail.GetProperty("tenant").GetProperty("id").GetGuid();
        var edit = JsonSerializer.SerializeToNode(input)!.AsObject();
        edit["tenantId"] = tenantId; edit["graceDays"] = 10;
        await Json(await pm.PutAsJsonAsync($"/api/leases/{id}", edit));
        edit["charges"]![0]!["amount"] = 1m;
        (await Fails(await pm.PutAsJsonAsync($"/api/leases/{id}", edit), HttpStatusCode.BadRequest)).Should().Contain("rent schedule");

        var ended = await Json(await pm.PostAsJsonAsync($"/api/leases/{id}/terminate", new { endDate = Today.AddMonths(13), reason = "Relocating" }));
        ended.GetProperty("status").GetString().Should().Be("terminated");
        detail = await Json(await pm.GetAsync($"/api/leases/{id}"));
        detail.GetProperty("unit").GetProperty("status").GetString().Should().Be("vacant");
        detail.GetProperty("lease").GetProperty("graceDays").GetInt32().Should().Be(10);
        detail.GetProperty("schedule").GetArrayLength().Should().Be(2, "rows after the end date are dropped");
    }

    [Fact]
    public async Task Leases_follow_row_level_security()
    {
        var admin = await f.LoginAsync("admin");
        var pm = await f.LoginAsync("pm");
        var all = (await Json(await admin.GetAsync("/api/leases?pageSize=1"))).GetProperty("total").GetInt32();
        var mine = await Json(await pm.GetAsync("/api/leases?pageSize=200&currency=USD"));
        mine.GetProperty("total").GetInt32().Should().Be(0, "the property manager has no USD (Mombasa Road) properties");
        (await Json(await pm.GetAsync("/api/leases?pageSize=1"))).GetProperty("total").GetInt32().Should().BeLessThan(all);
        var usd = (await Json(await admin.GetAsync("/api/leases?currency=USD&pageSize=1"))).GetProperty("rows")[0].GetProperty("id").GetGuid();
        (await pm.GetAsync($"/api/leases/{usd}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Seeded_leases_have_schedules_that_match_their_rent_charge()
    {
        var mismatched = await WithDb(db => db.Leases.Where(l => l.Status == "active")
            .Where(l => db.RentSchedules.Any(r => r.LeaseId == l.Id && r.Status == "current" &&
                r.MonthlyRent != l.Charges.Where(c => c.ChargeCode == "RENT" || c.ChargeCode == "RENTCOM").Sum(c => c.Amount)))
            .CountAsync());
        mismatched.Should().Be(0);
        (await WithDb(db => db.RentSchedules.CountAsync(r => r.Status == "pending_approval"))).Should().BeGreaterThan(0);
    }
}
