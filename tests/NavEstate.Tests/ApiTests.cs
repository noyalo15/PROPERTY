using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NavEstate.Api.Bc;
using NavEstate.Api.Data;
using OtpNet;
using Xunit;

namespace NavEstate.Tests;

[Collection("api")]
public class ApiTests(ApiFactory f)
{
    static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        r.IsSuccessStatusCode.Should().BeTrue($"{(int)r.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement;
    }

    T Scoped<T>(Func<IServiceProvider, T> f2)
    {
        using var scope = f.Services.CreateScope();
        return f2(scope.ServiceProvider);
    }

    async Task<T> WithDb<T>(Func<NavEstateDbContext, Task<T>> fn)
    {
        using var scope = f.Services.CreateScope();
        return await fn(scope.ServiceProvider.GetRequiredService<NavEstateDbContext>());
    }

    /// <summary>Runs the worker until nothing due is left (the seed queues 220 tenants; a run takes 50).</summary>
    async Task RunWorker()
    {
        for (var i = 0; i < 20; i++)
        {
            using var scope = f.Services.CreateScope();
            var r = await scope.ServiceProvider.GetRequiredService<BcSyncWorker>().ProcessDueAsync();
            if (r.Processed == 0) return;
        }
    }

    static object NewTenant(string name, string phone, string? kra = "A123456789B") => new
    {
        type = "individual", fullName = name, idOrPassportNo = "29876543", kraPin = kra, phone, email = "t@example.com", blacklisted = false
    };

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var r = await f.Client().GetAsync("/api/properties");
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Writes_without_the_csrf_header_are_rejected()
    {
        var c = f.CreateClient();
        var r = await c.PostAsJsonAsync("/api/auth/login", new { email = "admin@navestate.local", password = ApiFactory.DemoPassword });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("missing_csrf_header");
    }

    [Fact]
    public async Task Row_level_security_limits_properties_to_assignments()
    {
        var admin = await Json(await (await f.LoginAsync("admin")).GetAsync("/api/properties"));
        var pm = await Json(await (await f.LoginAsync("pm")).GetAsync("/api/properties"));
        var caretaker = await Json(await (await f.LoginAsync("caretaker")).GetAsync("/api/properties"));

        admin.GetArrayLength().Should().Be(5);
        pm.EnumerateArray().Select(p => p.GetProperty("code").GetString()).Should().BeEquivalentTo(["RIVERSIDE", "NGONGVIEW", "THIKAGREENS"]);
        caretaker.EnumerateArray().Select(p => p.GetProperty("code").GetString()).Should().BeEquivalentTo(["RIVERSIDE"]);

        // A unit in a property the user is not assigned to is invisible (404), not forbidden.
        var mrbp = admin.EnumerateArray().First(p => p.GetProperty("code").GetString() == "MRBP").GetProperty("id").GetString();
        (await (await f.LoginAsync("pm")).GetAsync($"/api/properties/{mrbp}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The database itself enforces it: a raw query with the PM's session context sees only 3 properties.
        var pmId = await WithDb(db => db.Users.Where(u => u.Email == "pm@navestate.local").Select(u => u.Id).SingleAsync());
        var count = await WithDb(async db =>
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("EXEC sp_set_session_context N'user_id', {0}; EXEC sp_set_session_context N'rls_bypass', 0;", pmId);
            return await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM core.properties").SingleAsync();
        });
        count.Should().Be(3);
    }

    [Fact]
    public async Task Seed_matches_the_spec_demo_data()
    {
        var admin = await f.LoginAsync("admin");
        var props = await Json(await admin.GetAsync("/api/properties"));
        int Units(string code) => props.EnumerateArray().First(p => p.GetProperty("code").GetString() == code).GetProperty("units").GetInt32();
        Units("RIVERSIDE").Should().Be(120);
        Units("MRBP").Should().Be(42);
        props.EnumerateArray().First(p => p.GetProperty("code").GetString() == "MRBP").GetProperty("defaultCurrency").GetString().Should().Be("USD");
        var tenants = await Json(await admin.GetAsync("/api/tenants?pageSize=1"));
        tenants.GetProperty("rows")[0].GetProperty("tenantNo").GetString().Should().MatchRegex("^T\\d{5}$");
    }

    [Fact]
    public async Task Creating_a_tenant_numbers_it_queues_UpsertTenant_and_the_worker_syncs_it()
    {
        var pm = await f.LoginAsync("pm");
        var created = await Json(await pm.PostAsJsonAsync("/api/tenants", NewTenant("Jane Wanjiru Test", "0799 111 222")));
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("tenantNo").GetString().Should().MatchRegex("^T\\d{5}$");

        var queued = await WithDb(db => db.BcSyncQueue.SingleAsync(q => q.EntityId == id));
        queued.FunctionName.Should().Be("UpsertTenant");
        queued.Status.Should().Be("pending");
        BcSyncQueue.Deserialize(queued.Payload).Select(p => p.Name).Should().Equal(
            "tenantNo", "name", "phone", "email", "kRAPIN", "currencyCode", "custPostingGroup", "vATBusPostingGroup", "propertyCode", "unitCode");
        BcSyncQueue.Deserialize(queued.Payload).Single(p => p.Name == "phone").Value.Should().Be("+254799111222");

        await RunWorker();

        var t = await Json(await pm.GetAsync($"/api/tenants/{id}"));
        t.GetProperty("tenant").GetProperty("bcSyncedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        t.GetProperty("bcSync").GetProperty("status").GetString().Should().Be("success");
    }

    [Fact]
    public async Task Failed_BC_calls_retry_with_backoff_and_give_up_after_five_attempts()
    {
        var pm = await f.LoginAsync("pm");
        var id = (await Json(await pm.PostAsJsonAsync("/api/tenants", NewTenant("Retry Test", "0799 333 444")))).GetProperty("id").GetGuid();
        await RunWorker(); // drain anything else first
        var mock = (MockBcClient)f.Services.GetRequiredService<IBcClient>();
        await WithDb(async db => { await db.BcSyncQueue.Where(q => q.EntityId == id).ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, "pending").SetProperty(q => q.Attempts, 0)); return 0; });

        mock.FailNext = 1; mock.FailMessage = "ERR:Customer Posting Group TENANTS does not exist.";
        await RunWorker();
        var row = await WithDb(db => db.BcSyncQueue.AsNoTracking().SingleAsync(q => q.EntityId == id));
        row.Status.Should().Be("failed");
        row.Attempts.Should().Be(1);
        row.LastError.Should().Contain("Posting Group");
        (row.NextAttemptAt!.Value - row.ProcessedAt!.Value).Should().BeCloseTo(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));

        // Not due yet: the next run leaves it alone.
        await RunWorker();
        (await WithDb(db => db.BcSyncQueue.AsNoTracking().SingleAsync(q => q.EntityId == id))).Attempts.Should().Be(1);

        // Exhaust the attempts.
        for (var i = 2; i <= 5; i++)
        {
            await WithDb(async db => { await db.BcSyncQueue.Where(q => q.EntityId == id).ExecuteUpdateAsync(s => s.SetProperty(q => q.NextAttemptAt, DateTimeOffset.UtcNow.AddMinutes(-1))); return 0; });
            mock.FailNext = 1;
            await RunWorker();
        }
        row = await WithDb(db => db.BcSyncQueue.AsNoTracking().SingleAsync(q => q.EntityId == id));
        row.Attempts.Should().Be(5);
        row.NextAttemptAt.Should().BeNull("five attempts is the limit");

        // The Retry button gives it a fresh set of attempts.
        var admin = await f.LoginAsync("admin");
        await Json(await admin.PostAsync($"/api/bc/queue/retry?id={row.Id}", null));
        await RunWorker();
        (await WithDb(db => db.BcSyncQueue.AsNoTracking().SingleAsync(q => q.EntityId == id))).Status.Should().Be("success");
    }

    [Fact]
    public async Task A_row_waits_for_the_row_it_depends_on()
    {
        var (parent, child) = await WithDb(async db =>
        {
            var p = new NavEstate.Api.Domain.BcSyncQueueItem { EntityType = "tenant", EntityId = Guid.NewGuid(), FunctionName = "UpsertTenant", ExternalRef = "T99998", Payload = "[]", Status = "failed", Attempts = 5 };
            db.BcSyncQueue.Add(p);
            var c = new NavEstate.Api.Domain.BcSyncQueueItem { EntityType = "bill", EntityId = Guid.NewGuid(), FunctionName = "PostInvoice", ExternalRef = "INV-TEST-1", Payload = "[]", DependsOnId = p.Id };
            db.BcSyncQueue.Add(c);
            await db.SaveChangesAsync();
            return (p.Id, c.Id);
        });
        await RunWorker();
        (await WithDb(db => db.BcSyncQueue.AsNoTracking().SingleAsync(q => q.Id == child))).Status.Should().Be("pending");
        await WithDb(async db => { await db.BcSyncQueue.Where(q => q.Id == parent).ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, "success")); return 0; });
        await RunWorker();
        (await WithDb(db => db.BcSyncQueue.AsNoTracking().SingleAsync(q => q.Id == child))).Status.Should().Be("success");
    }

    [Fact]
    public async Task Number_series_never_hands_out_the_same_number_twice()
    {
        var tasks = Enumerable.Range(0, 25).Select(_ => Task.Run(async () =>
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NavEstateDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var n = await scope.ServiceProvider.GetRequiredService<NumberSeriesService>().NextMonthlyAsync("TST", new DateOnly(2026, 11, 1));
            await tx.CommitAsync();
            return n;
        }));
        var numbers = await Task.WhenAll(tasks);
        numbers.Should().OnlyHaveUniqueItems();
        numbers.Should().Contain("TST-2026-11-000001").And.Contain("TST-2026-11-000025");
    }

    [Fact]
    public async Task Tenant_validation_and_audit_log()
    {
        var pm = await f.LoginAsync("pm");
        var bad = await pm.PostAsJsonAsync("/api/tenants", NewTenant("Bad Pin", "0799 555 666", kra: "12345"));
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("KRA PIN");

        var id = (await Json(await pm.PostAsJsonAsync("/api/tenants", NewTenant("Audit Person", "0799 777 888")))).GetProperty("id").GetGuid();
        await Json(await pm.PutAsJsonAsync($"/api/tenants/{id}", new
        {
            type = "individual", fullName = "Audit Person Renamed", idOrPassportNo = "29876543", kraPin = "A123456789B", phone = "0799 777 888",
            email = "t@example.com", blacklisted = true, blacklistReason = "Bounced cheques"
        }));
        var audit = await Json(await pm.GetAsync($"/api/tenants/{id}/audit"));
        audit.GetArrayLength().Should().Be(2);
        var update = audit.EnumerateArray().First(a => a.GetProperty("action").GetString() == "update");
        update.GetProperty("newValues").GetString().Should().Contain("Audit Person Renamed").And.Contain("blacklist_reason");
        update.GetProperty("user").GetString().Should().Contain("Property Manager");
    }

    [Fact]
    public async Task Roles_guard_setup_and_operations()
    {
        var pm = await f.LoginAsync("pm");
        var cashier = await f.LoginAsync("cashier");
        var accountant = await f.LoginAsync("accounts");
        var rule = new { name = "Test rule", basis = "fixed", value = 500, frequency = "once", capType = "none", graceDays = 5 };

        (await pm.PostAsJsonAsync("/api/setup/penalty-rules", rule)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await accountant.PostAsJsonAsync("/api/setup/penalty-rules", rule)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await cashier.PostAsJsonAsync("/api/tenants", NewTenant("Nope", "0799 000 111"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await pm.GetAsync("/api/setup/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await pm.GetAsync("/api/bc/queue")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var tenantUser = await f.LoginAsync("tenant");
        (await tenantUser.GetAsync("/api/properties")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "tenants only use the portal");
    }

    [Fact]
    public async Task Units_cannot_be_marked_occupied_by_hand_and_import_validates_everything_first()
    {
        var pm = await f.LoginAsync("pm");
        var props = await Json(await pm.GetAsync("/api/properties"));
        var thika = props.EnumerateArray().First(p => p.GetProperty("code").GetString() == "THIKAGREENS").GetProperty("id").GetGuid();

        var r = await pm.PostAsJsonAsync("/api/units", new { propertyId = thika, code = "TG-900", unitType = "maisonette", status = "occupied", defaultRent = 30000, defaultCurrency = "KES" });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        async Task<JsonElement> Import(string csv, bool dryRun)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
            file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            form.Add(file, "file", "units.csv");
            return await Json(await pm.PostAsync($"/api/units/import/{thika}?dryRun={dryRun}", form));
        }

        var withError = await Import("code,unit_type,default_rent\nTG-901,maisonette,30000\nTG-902,castle,1\n", false);
        withError.GetProperty("errors").GetArrayLength().Should().Be(1);
        (await WithDb(db => db.Units.CountAsync(u => u.Code == "TG-901"))).Should().Be(0, "nothing is saved when any row fails");

        var ok = await Import("code,block,unit_type,default_rent\nTG-901,Phase 3,maisonette,30000\nTG-902,Phase 3,maisonette,31000\n", false);
        ok.GetProperty("created").GetInt32().Should().Be(2);
        (await WithDb(db => db.Blocks.CountAsync(b => b.Name == "Phase 3"))).Should().Be(1);
    }

    [Fact]
    public async Task Tenant_signs_in_with_an_sms_code()
    {
        var phone = await WithDb(db => db.Users.Where(u => u.Email == "tenant@navestate.local").Select(u => u.PhoneNumber).SingleAsync());
        var c = f.Client();
        await Json(await c.PostAsJsonAsync("/api/auth/otp/request", new { phone }));
        string sms;
        lock (f.Sms.Last) sms = f.Sms.Last[phone!];
        var code = System.Text.RegularExpressions.Regex.Match(sms, "\\d{6}").Value;

        (await c.PostAsJsonAsync("/api/auth/otp/verify", new { phone, code = "000000" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await Json(await c.PostAsJsonAsync("/api/auth/otp/verify", new { phone, code }));
        var me = await Json(await c.GetAsync("/api/auth/me"));
        me.GetProperty("roles")[0].GetString().Should().Be("tenant");

        // Unknown numbers get the same answer, so the endpoint does not reveal who is a tenant.
        await Json(await f.Client().PostAsJsonAsync("/api/auth/otp/request", new { phone = "0700 000 001" }));
    }

    [Fact]
    public async Task Staff_must_enrol_in_two_factor_and_then_need_the_code_to_sign_in()
    {
        await using var strict = new ApiFactory { RequireStaffTwoFactor = true };
        await strict.InitializeAsync();
        var c = await strict.LoginAsync("cashier");

        var blocked = await c.GetAsync("/api/properties");
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("two_factor_enrolment_required");
        (await Json(await c.GetAsync("/api/auth/me"))).GetProperty("mustEnrolTwoFactor").GetBoolean().Should().BeTrue();

        var setup = await Json(await c.PostAsync("/api/auth/2fa/setup", null));
        var key = setup.GetProperty("sharedKey").GetString()!;
        var totp = new Totp(Base32Encoding.ToBytes(key));
        await Json(await c.PostAsJsonAsync("/api/auth/2fa/enable", new { code = totp.ComputeTotp() }));
        (await c.GetAsync("/api/properties")).StatusCode.Should().Be(HttpStatusCode.OK);

        var fresh = strict.Client();
        var login = await Json(await fresh.PostAsJsonAsync("/api/auth/login", new { email = "cashier@navestate.local", password = ApiFactory.DemoPassword }));
        login.GetProperty("status").GetString().Should().Be("requires_2fa");
        (await fresh.GetAsync("/api/properties")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await Json(await fresh.PostAsJsonAsync("/api/auth/login-2fa", new { code = totp.ComputeTotp() }));
        (await fresh.GetAsync("/api/properties")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
