using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NavEstate.Api.Auth;
using NavEstate.Api.Bc;
using NavEstate.Api.Common;
using NavEstate.Api.Domain;
using NavEstate.Api.Features;

namespace NavEstate.Api.Data;

public class SeedOptions
{
    public const string Section = "Seed";
    /// <summary>Super admin created on first start. Set the password in server config, never in git.</summary>
    public string AdminEmail { get; set; } = "admin@navestate.local";
    public string AdminPassword { get; set; } = "";
    /// <summary>Loads the demo landlords, properties, units, tenants and leases (development and demos only).</summary>
    public bool Demo { get; set; }
    /// <summary>Password for the demo users (one per role). Development only.</summary>
    public string DemoPassword { get; set; } = "";
}

/// <summary>Reference data (always) and demo data (Seed:Demo = true). Safe to run on every start.</summary>
public class Seeder(NavEstateDbContext db, UserManager<AppUser> users, RoleManager<IdentityRole> roles,
    NumberSeriesService numbers, BcSyncQueue bcQueue, RentScheduleService schedules, TimeProvider clock, ILogger<Seeder> log)
{
    public async Task RunAsync(SeedOptions o, CancellationToken ct = default)
    {
        await SeedReferenceAsync(ct);
        foreach (var r in Roles.All)
            if (!await roles.RoleExistsAsync(r)) await roles.CreateAsync(new IdentityRole(r));

        if (!string.IsNullOrEmpty(o.AdminPassword))
            await EnsureUserAsync(o.AdminEmail, "System Administrator", o.AdminPassword, Roles.SuperAdmin);
        else if (!await db.Users.AnyAsync(ct))
            log.LogWarning("No users exist and Seed:AdminPassword is empty: set it to create the first super admin.");

        if (o.Demo && !await db.Properties.AnyAsync(ct))
            await SeedDemoAsync(o, ct);
    }

    async Task<AppUser> EnsureUserAsync(string email, string name, string password, string role, string? phone = null,
        Guid? tenantId = null, Guid? landlordId = null)
    {
        var u = await users.FindByEmailAsync(email);
        if (u is null)
        {
            u = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = name, PhoneNumber = phone,
                PhoneNumberConfirmed = phone != null, CreatedAt = clock.Now(), LockoutEnabled = true, TenantId = tenantId, LandlordId = landlordId };
            var r = await users.CreateAsync(u, password);
            if (!r.Succeeded) throw new InvalidOperationException($"Could not create {email}: {string.Join(" ", r.Errors.Select(e => e.Description))}");
        }
        if (!await users.IsInRoleAsync(u, role)) await users.AddToRoleAsync(u, role);
        return u;
    }

    async Task SeedReferenceAsync(CancellationToken ct)
    {
        void Upsert<T>(DbSet<T> set, Func<T, bool> exists, T value) where T : class
        {
            if (!set.Local.Any(exists) && !set.AsEnumerable().Any(exists)) set.Add(value);
        }

        Upsert(db.Currencies, c => c.Code == "KES", new Currency { Code = "KES", Name = "Kenya Shilling", BcCode = "" });
        Upsert(db.Currencies, c => c.Code == "USD", new Currency { Code = "USD", Name = "US Dollar", BcCode = "USD" });

        (string Code, string Desc, bool IsRent, int Prio)[] codes =
        [
            ("PENALTY", "Late payment penalty", false, 1), ("WATER", "Water", false, 2), ("GARBAGE", "Garbage collection", false, 2),
            ("SERVICE", "Service charge", false, 3), ("SECURITY", "Security", false, 3), ("PARKING", "Parking", false, 3),
            ("REPAIR", "Repairs recharged", false, 3), ("OTHER", "Other charges", false, 3),
            ("RENT", "Residential rent", true, 4), ("RENTCOM", "Commercial rent", true, 4)
        ];
        foreach (var c in codes)
            Upsert(db.ChargeCodes, x => x.Code == c.Code, new ChargeCode { Code = c.Code, Description = c.Desc, IsRent = c.IsRent, AllocationPriority = c.Prio });

        (string Code, string Name, string Bc, string Cur)[] channels =
        [
            ("MPESA", "M-PESA Paybill", "MPESA", "KES"), ("BANK-KES", "Bank (KES account)", "BANK", "KES"),
            ("BANK-USD", "Bank (USD account)", "BANK", "USD"), ("CHEQUE", "Cheque", "CHEQUE", "KES"), ("CASH", "Cash (office)", "CASH", "KES")
        ];
        foreach (var c in channels)
            Upsert(db.PaymentChannels, x => x.Code == c.Code, new PaymentChannel { Code = c.Code, Name = c.Name, BcChannelCode = c.Bc, CurrencyCode = c.Cur });

        var settings = new Dictionary<string, object>
        {
            ["company_name"] = "NavEstate Demo Company", ["company_logo"] = "", ["company_address"] = "Nairobi, Kenya", ["company_kra_pin"] = "",
            ["paybill_number"] = "", ["allocation_order"] = new[] { "PENALTY", "WATER", "SERVICE", "RENT" },
            ["vat_rate"] = 16, ["wht_rate"] = 10, ["mri_rate"] = 7.5, ["penalty_run_time"] = "01:00", ["reminder_days"] = 3, ["sms_sender_id"] = ""
        };
        foreach (var (k, v) in settings)
            Upsert(db.Settings, x => x.Key == k, new Setting { Key = k, Value = JsonSerializer.Serialize(v) });

        (string Code, string Body)[] templates =
        [
            ("invoice_issued", "Dear {{tenant_name}}, your {{period}} invoice {{bill_no}} of {{amount}} for {{unit}} is due on {{due_date}}. Pay via M-PESA Paybill {{paybill}}, account {{unit}}."),
            ("payment_received", "Dear {{tenant_name}}, we have received {{amount}} ({{channel}} {{reference}}). Receipt {{receipt_no}}. Balance: {{balance}}. Thank you."),
            ("reminder_before_due", "Dear {{tenant_name}}, a reminder that {{amount}} for {{unit}} is due on {{due_date}}. Paybill {{paybill}}, account {{unit}}."),
            ("overdue_penalty_warning", "Dear {{tenant_name}}, {{amount}} for {{unit}} is overdue. A late payment penalty applies after {{grace_end}}. Please pay to Paybill {{paybill}}."),
            ("escalation_notice", "Dear {{tenant_name}}, as per your lease {{lease_no}}, rent for {{unit}} changes from {{old_rent}} to {{new_rent}} from {{effective_from}}."),
            ("advice_slip_link", "Dear {{tenant_name}}, your {{slip_type}} advice slip {{slip_no}} is ready: {{link}}"),
            ("maintenance_status", "Dear {{tenant_name}}, your repair request {{request_no}} is now {{status}}.")
        ];
        foreach (var t in templates)
            Upsert(db.NotificationTemplates, x => x.Code == t.Code && x.Channel == "sms", new NotificationTemplate { Code = t.Code, Channel = "sms", Body = t.Body });

        if (!db.PenaltyRules.Any())
            db.PenaltyRules.AddRange(
                new PenaltyRule { Name = "10% of overdue rent, once", Basis = "percent_overdue_rent", Value = 10, Frequency = "once", CapType = "none", GraceDays = 5 },
                new PenaltyRule { Name = "KES 1,000 flat per month", Basis = "fixed", Value = 1000, Frequency = "monthly", CapType = "none", GraceDays = 5 },
                new PenaltyRule { Name = "2% of balance monthly, max 10% of rent", Basis = "percent_overdue_balance", Value = 2, Frequency = "monthly", CapType = "percent_of_rent", CapValue = 10, GraceDays = 5 });

        if (!db.WaterTariffs.Any())
            db.WaterTariffs.Add(new WaterTariff
            {
                Name = "Standard residential", StandingCharge = 200, EffectiveFrom = new DateOnly(2026, 1, 1),
                Bands = [new() { FromM3 = 0, ToM3 = 6, Rate = 60 }, new() { FromM3 = 6, ToM3 = 20, Rate = 85 }, new() { FromM3 = 20, ToM3 = 50, Rate = 110 }, new() { FromM3 = 50, Rate = 130 }]
            });

        if (!db.ExchangeRates.Any())
            db.ExchangeRates.Add(new ExchangeRate { CurrencyCode = "USD", RateDate = clock.Today(), KesPerUnit = 129.50m, Source = "MANUAL" });

        await db.SaveChangesAsync(ct);
    }

    // ---------------- demo data ----------------

    static readonly string[] First = ["Wanjiku", "Otieno", "Achieng", "Kamau", "Njeri", "Mutua", "Akinyi", "Kipchoge", "Wairimu", "Omondi", "Chebet", "Mwangi", "Atieno", "Kiprono", "Nyambura", "Ochieng", "Jepkosgei", "Kariuki", "Moraa", "Barasa", "Wambui", "Onyango", "Muthoni", "Kibet", "Adhiambo", "Njoroge", "Nekesa", "Gitau", "Awino", "Rotich", "Waceke", "Odhiambo", "Kerubo", "Macharia", "Naliaka", "Cherono"];
    static readonly string[] Last = ["Kamau", "Otieno", "Mwangi", "Ochieng", "Kariuki", "Wanjala", "Kiprotich", "Njoroge", "Odhiambo", "Mutiso", "Kimani", "Owino", "Chege", "Koech", "Gichuru", "Mburu", "Nyaga", "Wekesa", "Muriuki", "Omollo", "Langat", "Kiplagat", "Ndungu", "Okoth", "Maina", "Wafula", "Ruto", "Kilonzo"];
    static readonly string[] Companies = ["Savanna Logistics Ltd", "Tausi Pharmaceuticals Ltd", "Jambo Freight Forwarders Ltd", "Mawingu Tech Solutions Ltd", "Pwani Agro Exporters Ltd", "Simba Hardware Ltd", "Kilele Insurance Brokers Ltd", "Baraka Foods Ltd", "Nyota Electricals Ltd", "Upendo Clinics Ltd", "Zuri Interiors Ltd", "Mto Packaging Ltd", "Imara Engineering Ltd", "Bahari Cold Storage Ltd", "Tumaini Microfinance Ltd", "Kifaru Motors Ltd", "Neema Textiles Ltd", "Safari Couriers Ltd", "Amani Legal Advocates LLP", "Jua Kali Solar Ltd"];

    record UnitSpec(string Code, string? Block, string Type, string Floor, decimal Rent, decimal? Area);

    async Task SeedDemoAsync(SeedOptions o, CancellationToken ct)
    {
        log.LogInformation("Seeding demo data");
        var rnd = new Random(20261115);
        var today = clock.Today();

        var l1 = new Landlord { Name = "Kilimani Heights Holdings Ltd", Type = "company", KraPin = "P051234567K", Phone = "+254722100200", Email = "accounts@kilimaniheights.example", CommissionPercent = 8, BankDetails = "KCB Kilimani, A/C 1100223344" };
        var l2 = new Landlord { Name = "Grace Wanjiru Njoroge", Type = "individual", KraPin = "A004567891Z", Phone = "+254733445566", Email = "grace.njoroge@example.com", CommissionPercent = 10, BankDetails = "Equity Karen, A/C 0450199887766" };
        db.Landlords.AddRange(l1, l2);

        var riverside = new Property { Landlord = l1, Code = "RIVERSIDE", Name = "Riverside Apartments Kilimani", Type = "residential", Location = "Riverside Drive, Kilimani", County = "Nairobi", BulkWaterMeterNo = "NCWSC-BULK-0091", DefaultCurrency = "KES" };
        var mrbp = new Property { Landlord = l1, Code = "MRBP", Name = "Mombasa Road Business Park", Type = "commercial", Location = "Mombasa Road, Syokimau", County = "Machakos", DefaultCurrency = "USD" };
        var ngong = new Property { Landlord = l2, Code = "NGONGVIEW", Name = "Ngong View Court Karen", Type = "residential", Location = "Ngong Road, Karen", County = "Nairobi", DefaultCurrency = "KES" };
        var westlands = new Property { Landlord = l1, Code = "WESTCORNER", Name = "Westlands Corner Plaza", Type = "mixed", Location = "Waiyaki Way, Westlands", County = "Nairobi", DefaultCurrency = "KES" };
        var thika = new Property { Landlord = l2, Code = "THIKAGREENS", Name = "Thika Greens Estate", Type = "residential", Location = "Thika Road, Thika", County = "Kiambu", DefaultCurrency = "KES" };
        db.Properties.AddRange(riverside, mrbp, ngong, westlands, thika);

        // Riverside: blocks A-C, floors 1-4, 10 units per floor = 120 units (A-101 … C-410).
        var riversideUnits = new List<UnitSpec>();
        foreach (var b in new[] { "A", "B", "C" })
            for (var f = 1; f <= 4; f++)
                for (var n = 1; n <= 10; n++)
                {
                    var type = n <= 4 ? "1br" : n <= 8 ? "2br" : "3br";
                    var rent = type switch { "1br" => 32000m, "2br" => 45000m, _ => 62000m } + (f - 1) * 1000m;
                    riversideUnits.Add(new UnitSpec($"{b}-{f}{n:D2}", b, type, f.ToString(), rent, type switch { "1br" => 550, "2br" => 850, _ => 1200 }));
                }
        // Mombasa Road Business Park: 42 units in USD.
        var mrbpUnits = new List<UnitSpec>();
        for (var i = 1; i <= 12; i++) mrbpUnits.Add(new UnitSpec($"W-{i:D2}", "Warehouses", "warehouse", "G", 3500 + (i % 4) * 500, 10000 + (i % 4) * 2500));
        for (var f = 1; f <= 3; f++) for (var n = 1; n <= 8; n++) mrbpUnits.Add(new UnitSpec($"O-{f}{n:D2}", "Office Block", "office", f.ToString(), 1200 + (n % 3) * 400, 1500 + (n % 3) * 600));
        for (var i = 1; i <= 6; i++) mrbpUnits.Add(new UnitSpec($"S-{i:D2}", "Shops", "shop", "G", 900, 600));
        // Ngong View Court: 24 apartments.
        var ngongUnits = Enumerable.Range(1, 24).Select(i => new UnitSpec($"NV-{i:D2}", null, i % 3 == 0 ? "3br" : "2br", ((i - 1) / 6 + 1).ToString(), i % 3 == 0 ? 85000 : 65000, i % 3 == 0 ? 1500 : 1100)).ToList();
        // Westlands Corner Plaza: shops and offices on the lower floors, apartments above (mixed use).
        var westUnits = new List<UnitSpec>();
        for (var i = 1; i <= 8; i++) westUnits.Add(new UnitSpec($"G-{i:D2}", null, i == 8 ? "kiosk" : "shop", "G", i == 8 ? 15000 : 55000, i == 8 ? 80 : 450));
        for (var i = 1; i <= 10; i++) westUnits.Add(new UnitSpec($"1-{i:D2}", null, "office", "1", 70000, 900));
        for (var i = 1; i <= 12; i++) westUnits.Add(new UnitSpec($"2-{i:D2}", null, i % 2 == 0 ? "1br" : "bedsitter", "2", i % 2 == 0 ? 38000 : 22000, i % 2 == 0 ? 500 : 320));
        // Thika Greens Estate: 36 maisonettes.
        var thikaUnits = Enumerable.Range(1, 36).Select(i => new UnitSpec($"TG-{i:D3}", i <= 18 ? "Phase 1" : "Phase 2", "maisonette", "G", i <= 18 ? 28000 : 30000, 1300)).ToList();

        var rentCodes = new HashSet<string>(["RENT", "RENTCOM"]);
        var tenantPhones = new HashSet<string>();
        var leaseCount = 0;
        Tenant? firstTenant = null;

        async Task Build(Property p, List<UnitSpec> specs, double occupancy)
        {
            var blocks = new Dictionary<string, Block>();
            foreach (var s in specs)
            {
                Block? block = null;
                if (s.Block is not null && !blocks.TryGetValue(s.Block, out block))
                {
                    block = new Block { Property = p, Name = s.Block };
                    blocks[s.Block] = block; db.Blocks.Add(block);
                }
                var commercial = s.Type is "shop" or "office" or "warehouse" or "kiosk";
                var cur = p.DefaultCurrency;
                var u = new Unit { Property = p, Block = block, Code = s.Code, UnitType = s.Type, Floor = s.Floor, AreaSqft = s.Area, DefaultRent = s.Rent, DefaultCurrency = cur, Status = "vacant" };
                db.Units.Add(u);

                var charges = new List<(string Code, decimal Amount)>();
                if (cur == "USD") charges.Add(("SERVICE", Math.Round(s.Rent * 0.12m / 10) * 10));
                else if (commercial) { charges.Add(("SERVICE", 6000)); charges.Add(("GARBAGE", 500)); }
                else { charges.Add(("SERVICE", s.Rent >= 60000 ? 5000 : 3000)); charges.Add(("GARBAGE", 300)); charges.Add(("SECURITY", 1000)); }
                foreach (var c in charges) db.UnitCharges.Add(new UnitCharge { Unit = u, ChargeCode = c.Code, Amount = c.Amount, CurrencyCode = cur, Frequency = "monthly" });
                if (!commercial || p.Code == "WESTCORNER")
                    db.Meters.Add(new Meter { Unit = u, MeterType = "water", SerialNo = $"WM-{p.Code[..3]}-{s.Code}", LastReading = rnd.Next(100, 900), LastReadingDate = today.AddDays(-today.Day + 1).AddDays(-1) });

                var roll = rnd.NextDouble();
                if (roll > occupancy) { if (roll > 0.985) u.Status = "maintenance"; continue; }

                // Occupied: tenant + active lease with the unit's charges.
                var t = new Tenant { TenantNo = await numbers.NextTenantNoAsync(ct) };
                if (commercial && rnd.NextDouble() < 0.8)
                {
                    var name = Companies[rnd.Next(Companies.Length)];
                    t.Type = "company"; t.FullName = name; t.CompanyRegNo = $"PVT-{rnd.Next(100000, 999999):X}"; t.ContactPerson = $"{First[rnd.Next(First.Length)]} {Last[rnd.Next(Last.Length)]}";
                    t.KraPin = $"P05{rnd.Next(1000000, 9999999)}{(char)('A' + rnd.Next(26))}";
                    t.Email = $"accounts@{new string(name.ToLowerInvariant().Where(char.IsLetter).Take(14).ToArray())}.example";
                }
                else
                {
                    var fn = First[rnd.Next(First.Length)]; var ln = Last[rnd.Next(Last.Length)];
                    t.Type = "individual"; t.FullName = $"{fn} {ln}"; t.IdOrPassportNo = rnd.Next(20000000, 39999999).ToString();
                    t.KraPin = $"A0{rnd.Next(10000000, 99999999)}{(char)('A' + rnd.Next(26))}";
                    t.Email = rnd.NextDouble() < 0.7 ? $"{fn.ToLowerInvariant()}.{ln.ToLowerInvariant()}{rnd.Next(10, 99)}@example.com" : null;
                    t.NextOfKinName = $"{First[rnd.Next(First.Length)]} {ln}"; t.NextOfKinPhone = $"+2547{rnd.Next(10000000, 99999999)}";
                }
                string phone;
                do phone = $"+2547{rnd.Next(0, 3)}{rnd.Next(1000000, 9999999)}"; while (!tenantPhones.Add(phone));
                t.Phone = phone;
                if (rnd.NextDouble() < 0.02) { t.Blacklisted = true; t.BlacklistReason = "Left previous unit with unpaid arrears"; }
                db.Tenants.Add(t);
                firstTenant ??= t;

                var start = today.AddDays(-rnd.Next(30, 3 * 365));
                start = new DateOnly(start.Year, start.Month, 1);
                var years = commercial ? 6 : rnd.Next(1, 3);
                var end = start.AddYears(years).AddDays(-1);
                while (end < today.AddDays(20)) end = end.AddYears(1);
                var lease = new Lease
                {
                    LeaseNo = await numbers.NextLeaseNoAsync(ct), Tenant = t, Unit = u, Status = "active", StartDate = start, EndDate = end,
                    CurrencyCode = cur, BillingDay = 1, Frequency = commercial && rnd.NextDouble() < 0.3 ? "quarterly" : "monthly",
                    DepositAmount = s.Rent * (commercial ? 3 : 1), WaterDepositAmount = commercial ? 0 : 2500, GraceDays = 5,
                    VatApplicable = commercial, WhtAgent = commercial && t.Type == "company" && rnd.NextDouble() < 0.5
                };
                lease.Charges.Add(new LeaseCharge { ChargeCode = commercial ? "RENTCOM" : "RENT", Description = commercial ? "Commercial rent" : "Rent", Amount = s.Rent, CurrencyCode = cur });
                foreach (var c in charges) lease.Charges.Add(new LeaseCharge { ChargeCode = c.Code, Description = c.Code switch { "SERVICE" => "Service charge", "GARBAGE" => "Garbage collection", _ => "Security" }, Amount = c.Amount, CurrencyCode = cur });
                db.Leases.Add(lease);
                u.Status = "occupied";

                // Most leases escalate yearly (rule 2). Past escalations are in effect; the next ones wait for notice and approval.
                EscalationInput? rule = rnd.NextDouble() switch
                {
                    < 0.55 => new("fixed_percent", commercial ? 7.5m : 5m, 12, true, commercial ? 90 : 60),
                    < 0.70 => new("fixed_percent", 5m, 12, false, 60),
                    < 0.80 => new("fixed_amount", cur == "USD" ? 100 : 1000, 12, true, 60),
                    _ => null
                };
                if (rule is not null)
                    db.RentEscalationRules.Add(new RentEscalationRule { LeaseId = lease.Id, Type = rule.Type, Value = rule.Value, EveryMonths = rule.EveryMonths, Compounding = rule.Compounding, NoticeDays = rule.NoticeDays });
                var rows = RentScheduleGenerator.Generate(s.Rent, start, end, rule).Select(r => new RentSchedule
                {
                    LeaseId = lease.Id, EffectiveFrom = r.From, EffectiveTo = r.To, MonthlyRent = r.MonthlyRent, ChangeLabel = r.Label, Status = "scheduled"
                }).ToList();
                db.RentSchedules.AddRange(rows);
                foreach (var r in rows.Where((r, i) => i == 0 || r.EffectiveFrom <= today))
                {
                    RentScheduleService.MakeCurrent(lease, rows, r, rentCodes);
                }
                leaseCount++;
                db.Deposits.Add(new Deposit { TenantId = t.Id, LeaseId = lease.Id, DepositType = "rent", CurrencyCode = cur, AmountHeld = lease.DepositAmount, Status = "held" });
                if (lease.WaterDepositAmount > 0)
                    db.Deposits.Add(new Deposit { TenantId = t.Id, LeaseId = lease.Id, DepositType = "water", CurrencyCode = cur, AmountHeld = lease.WaterDepositAmount, Status = "held" });
            }
        }

        await Build(riverside, riversideUnits, 0.90);
        await Build(mrbp, mrbpUnits, 0.86);
        await Build(ngong, ngongUnits, 0.92);
        await Build(westlands, westUnits, 0.80);
        await Build(thika, thikaUnits, 0.83);
        await db.SaveChangesAsync(ct);

        // Flag the escalation notices that are already due so the demo shows the approval flow.
        await schedules.AdvanceAsync(today, null, ct);

        // Queue every demo tenant for BC (the mock client "posts" them on the first worker run).
        foreach (var t in db.Tenants.Local.ToList()) await bcQueue.EnqueueUpsertTenantAsync(t, ct);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(o.DemoPassword))
        {
            var pw = o.DemoPassword;
            await EnsureUserAsync("admin@navestate.local", "Demo Super Admin", pw, Roles.SuperAdmin);
            var pm = await EnsureUserAsync("pm@navestate.local", "Peter Mwangi (Property Manager)", pw, Roles.PropertyManager);
            var acc = await EnsureUserAsync("accounts@navestate.local", "Agnes Chebet (Accountant)", pw, Roles.Accountant);
            var cashier = await EnsureUserAsync("cashier@navestate.local", "Collins Otieno (Cashier)", pw, Roles.Cashier);
            var caretaker = await EnsureUserAsync("caretaker@navestate.local", "John Kariuki (Caretaker)", pw, Roles.Caretaker);
            await EnsureUserAsync("landlord@navestate.local", l2.Name, pw, Roles.Landlord, landlordId: l2.Id);
            if (firstTenant is not null)
                await EnsureUserAsync("tenant@navestate.local", firstTenant.FullName, pw, Roles.Tenant, phone: firstTenant.Phone, tenantId: firstTenant.Id);

            db.UserPropertyAssignments.AddRange(
                new UserPropertyAssignment { UserId = pm.Id, PropertyId = riverside.Id },
                new UserPropertyAssignment { UserId = pm.Id, PropertyId = ngong.Id },
                new UserPropertyAssignment { UserId = pm.Id, PropertyId = thika.Id },
                new UserPropertyAssignment { UserId = acc.Id, LandlordId = l1.Id },
                new UserPropertyAssignment { UserId = acc.Id, LandlordId = l2.Id },
                new UserPropertyAssignment { UserId = cashier.Id, LandlordId = l1.Id },
                new UserPropertyAssignment { UserId = caretaker.Id, PropertyId = riverside.Id });
            var landlordUser = await users.FindByEmailAsync("landlord@navestate.local");
            db.UserPropertyAssignments.Add(new UserPropertyAssignment { UserId = landlordUser!.Id, LandlordId = l2.Id });
            await db.SaveChangesAsync(ct);
        }
        log.LogInformation("Demo data: 2 landlords, 5 properties, {Units} units, {Leases} leases", db.Units.Local.Count, leaseCount);
    }
}
