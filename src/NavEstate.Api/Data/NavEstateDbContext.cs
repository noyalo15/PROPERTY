using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using NavEstate.Api.Domain;

namespace NavEstate.Api.Data;

public class NavEstateDbContext(DbContextOptions<NavEstateDbContext> options) : IdentityDbContext<AppUser>(options)
{
    // core
    public DbSet<Landlord> Landlords => Set<Landlord>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<UnitCharge> UnitCharges => Set<UnitCharge>();
    public DbSet<Meter> Meters => Set<Meter>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<PaymentChannel> PaymentChannels => Set<PaymentChannel>();
    // tenancy
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantDocument> TenantDocuments => Set<TenantDocument>();
    public DbSet<ChargeCode> ChargeCodes => Set<ChargeCode>();
    public DbSet<Lease> Leases => Set<Lease>();
    public DbSet<LeaseCharge> LeaseCharges => Set<LeaseCharge>();
    public DbSet<RentEscalationRule> RentEscalationRules => Set<RentEscalationRule>();
    public DbSet<RentSchedule> RentSchedules => Set<RentSchedule>();
    public DbSet<PenaltyRule> PenaltyRules => Set<PenaltyRule>();
    public DbSet<Deposit> Deposits => Set<Deposit>();
    public DbSet<DepositMovementRow> DepositMovements => Set<DepositMovementRow>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<AdviceSlip> AdviceSlips => Set<AdviceSlip>();
    // billing
    public DbSet<BillingRun> BillingRuns => Set<BillingRun>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<BillLine> BillLines => Set<BillLine>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<WaterReading> WaterReadings => Set<WaterReading>();
    public DbSet<WaterTariff> WaterTariffs => Set<WaterTariff>();
    public DbSet<WaterTariffBand> WaterTariffBands => Set<WaterTariffBand>();
    // pay
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<MpesaTransaction> MpesaTransactions => Set<MpesaTransaction>();
    public DbSet<BankStatementUpload> BankStatementUploads => Set<BankStatementUpload>();
    public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();
    // ops
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<MaintenanceRequest> MaintenanceRequests => Set<MaintenanceRequest>();
    public DbSet<MaintenancePhoto> MaintenancePhotos => Set<MaintenancePhoto>();
    public DbSet<PreventiveTask> PreventiveTasks => Set<PreventiveTask>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    // app
    public DbSet<BcSyncQueueItem> BcSyncQueue => Set<BcSyncQueueItem>();
    public DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();
    public DbSet<AuditLog> AuditLog => Set<AuditLog>();
    public DbSet<UserPropertyAssignment> UserPropertyAssignments => Set<UserPropertyAssignment>();
    public DbSet<BcPingLog> BcPingLog => Set<BcPingLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<decimal>().HavePrecision(18, 2);
        b.Properties<DateTimeOffset>().HavePrecision(0);
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        // Identity tables in the app schema.
        mb.Entity<AppUser>().ToTable("users", "app");
        mb.Entity<IdentityRole>().ToTable("roles", "app");
        mb.Entity<IdentityUserRole<string>>().ToTable("user_roles", "app");
        mb.Entity<IdentityUserClaim<string>>().ToTable("user_claims", "app");
        mb.Entity<IdentityUserLogin<string>>().ToTable("user_logins", "app");
        mb.Entity<IdentityUserToken<string>>().ToTable("user_tokens", "app");
        mb.Entity<IdentityRoleClaim<string>>().ToTable("role_claims", "app");

        // ---- core
        T<Landlord>(mb, "landlords", "core", e => Check(e, "type", V.LandlordType));
        T<Property>(mb, "properties", "core", e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne(x => x.Landlord).WithMany(x => x.Properties).HasForeignKey(x => x.LandlordId);
            Check(e, "type", V.PropertyType); Check(e, "default_currency", V.Currency);
        });
        T<Block>(mb, "blocks", "core", e =>
        {
            e.HasOne(x => x.Property).WithMany(x => x.Blocks).HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.PropertyId, x.Name }).IsUnique();
        });
        T<Unit>(mb, "units", "core", e =>
        {
            e.HasIndex(x => new { x.PropertyId, x.Code }).IsUnique();
            e.HasOne(x => x.Property).WithMany(x => x.Units).HasForeignKey(x => x.PropertyId);
            e.HasOne(x => x.Block).WithMany().HasForeignKey(x => x.BlockId);
            Check(e, "unit_type", V.UnitType); Check(e, "status", V.UnitStatus); Check(e, "default_currency", V.Currency);
        });
        T<UnitCharge>(mb, "unit_charges", "core", e =>
        {
            e.HasOne(x => x.Unit).WithMany(x => x.Charges).HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ChargeCode>().WithMany().HasForeignKey(x => x.ChargeCode);
            Check(e, "currency_code", V.Currency); Check(e, "frequency", V.ChargeFrequency);
        });
        T<Meter>(mb, "meters", "core", e =>
        {
            e.HasOne(x => x.Unit).WithMany(x => x.Meters).HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.LastReading).HasPrecision(18, 3);
            Check(e, "meter_type", V.MeterType);
        });
        Audited<Currency>(mb.Entity<Currency>().ToTable("currencies", "core")).HasKey(x => x.Code);
        T<ExchangeRate>(mb, "exchange_rates", "core", e =>
        {
            e.Property(x => x.KesPerUnit).HasPrecision(18, 6);
            e.HasIndex(x => new { x.CurrencyCode, x.RateDate }).IsUnique();
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyCode);
        });
        Audited<Setting>(mb.Entity<Setting>().ToTable("settings", "core", t => t.HasCheckConstraint("ck_settings_value_json", "ISJSON([value]) = 1 OR ISJSON(CONCAT('[', [value], ']')) = 1"))).HasKey(x => x.Key);
        T<PaymentChannel>(mb, "payment_channels", "core", e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            Check(e, "bc_channel_code", V.BcChannel); Check(e, "currency_code", V.Currency);
        });

        // ---- tenancy
        T<Tenant>(mb, "tenants", "tenancy", e =>
        {
            e.HasIndex(x => x.TenantNo).IsUnique();
            e.HasIndex(x => x.Phone);
            e.HasIndex(x => x.FullName);
            Check(e, "type", V.TenantType);
        });
        T<TenantDocument>(mb, "tenant_documents", "tenancy", e =>
        {
            e.HasOne(x => x.Tenant).WithMany(x => x.Documents).HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            Check(e, "doc_type", V.DocType);
        });
        Audited<ChargeCode>(mb.Entity<ChargeCode>().ToTable("charge_codes", "tenancy")).HasKey(x => x.Code);
        T<Lease>(mb, "leases", "tenancy", e =>
        {
            e.HasIndex(x => x.LeaseNo).IsUnique();
            e.HasOne(x => x.Tenant).WithMany(x => x.Leases).HasForeignKey(x => x.TenantId);
            e.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId);
            e.HasOne(x => x.PenaltyRule).WithMany().HasForeignKey(x => x.PenaltyRuleId);
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.TransferredToLeaseId);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.UnitId).IsUnique().HasFilter("[status] = 'active'").HasDatabaseName("ux_leases_one_active_per_unit");
            Check(e, "status", V.LeaseStatus); Check(e, "currency_code", V.Currency); Check(e, "frequency", V.Frequency);
            e.ToTable(t => t.HasCheckConstraint("ck_leases_billing_day", "[billing_day] BETWEEN 1 AND 28"));
        });
        T<LeaseCharge>(mb, "lease_charges", "tenancy", e =>
        {
            e.HasOne(x => x.Lease).WithMany(x => x.Charges).HasForeignKey(x => x.LeaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ChargeCode>().WithMany().HasForeignKey(x => x.ChargeCode);
            Check(e, "currency_code", V.Currency); Check(e, "frequency", V.ChargeFrequency);
        });
        T<RentEscalationRule>(mb, "rent_escalation_rules", "tenancy", e =>
        {
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.LeaseId).IsUnique();
            e.Property(x => x.Value).HasPrecision(18, 4);
            Check(e, "type", V.EscalationType);
        });
        T<RentSchedule>(mb, "rent_schedule", "tenancy", e =>
        {
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.LeaseId, x.EffectiveFrom }).IsUnique();
            e.HasIndex(x => x.Status).HasFilter("[status] IN ('scheduled', 'pending_approval', 'approved')");
            Check(e, "status", V.ScheduleStatus);
        });
        T<PenaltyRule>(mb, "penalty_rules", "tenancy", e =>
        {
            e.Property(x => x.Value).HasPrecision(18, 4);
            Check(e, "basis", V.PenaltyBasis); Check(e, "frequency", V.PenaltyFrequency); Check(e, "cap_type", V.PenaltyCap);
        });
        T<Deposit>(mb, "deposits", "tenancy", e =>
        {
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId);
            Check(e, "deposit_type", V.DepositType); Check(e, "status", V.DepositStatus); Check(e, "currency_code", V.Currency);
        });
        T<DepositMovementRow>(mb, "deposit_movements", "tenancy", e =>
        {
            e.HasOne<Deposit>().WithMany().HasForeignKey(x => x.DepositId);
            Check(e, "movement", V.DepositMovement);
        });
        T<Transfer>(mb, "transfers", "tenancy", e =>
        {
            e.HasIndex(x => x.TransferNo).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.FromLeaseId);
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.ToLeaseId);
            e.HasOne<Unit>().WithMany().HasForeignKey(x => x.ToUnitId);
            e.HasOne<AdviceSlip>().WithMany().HasForeignKey(x => x.AdviceSlipId);
            Check(e, "balance_option", V.BalanceOption); Check(e, "deposit_option", V.DepositOption); Check(e, "status", V.TransferStatus);
        });
        T<AdviceSlip>(mb, "advice_slips", "tenancy", e =>
        {
            e.HasIndex(x => x.SlipNo).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId);
            Check(e, "slip_type", V.SlipType); Json(e, "snapshot");
        });

        // ---- billing
        T<BillingRun>(mb, "billing_runs", "billing", e =>
        {
            e.HasIndex(x => x.RunNo).IsUnique();
            e.HasOne<Property>().WithMany().HasForeignKey(x => x.PropertyId);
            Check(e, "status", V.BillingRunStatus);
        });
        T<Bill>(mb, "bills", "billing", e =>
        {
            e.HasIndex(x => x.BillNo).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            e.HasOne<Unit>().WithMany().HasForeignKey(x => x.UnitId);
            e.HasOne<BillingRun>().WithMany().HasForeignKey(x => x.BillingRunId);
            e.Property(x => x.Balance).HasComputedColumnSql("[total_amount] - [amount_paid] - [amount_credited]", stored: true);
            e.Property(x => x.RowVersion).IsRowVersion();
            Check(e, "bill_type", V.BillType); Check(e, "status", V.BillStatus); Check(e, "currency_code", V.Currency);
        });
        T<BillLine>(mb, "bill_lines", "billing", e =>
        {
            e.HasOne(x => x.Bill).WithMany(x => x.Lines).HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ChargeCode>().WithMany().HasForeignKey(x => x.ChargeCode);
            e.Property(x => x.Quantity).HasPrecision(18, 3);
        });
        T<CreditNote>(mb, "credit_notes", "billing", e =>
        {
            e.HasIndex(x => x.CreditNo).IsUnique();
            e.HasOne<Bill>().WithMany().HasForeignKey(x => x.BillId);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            Check(e, "status", V.CreditStatus); Check(e, "currency_code", V.Currency);
        });
        T<WaterReading>(mb, "water_readings", "billing", e =>
        {
            e.HasOne<Meter>().WithMany().HasForeignKey(x => x.MeterId);
            e.HasOne<Unit>().WithMany().HasForeignKey(x => x.UnitId);
            e.HasIndex(x => new { x.MeterId, x.Period }).IsUnique();
            e.Property(x => x.PreviousReading).HasPrecision(18, 3);
            e.Property(x => x.CurrentReading).HasPrecision(18, 3);
            e.Property(x => x.Consumption).HasPrecision(18, 3).HasComputedColumnSql("[current_reading] - [previous_reading]", stored: true);
            e.Property(x => x.RatePerM3).HasPrecision(18, 4);
            Check(e, "flag", V.WaterFlag); Check(e, "status", V.WaterStatus);
        });
        T<WaterTariff>(mb, "water_tariffs", "billing", e => e.HasOne<Property>().WithMany().HasForeignKey(x => x.PropertyId));
        T<WaterTariffBand>(mb, "water_tariff_bands", "billing", e =>
        {
            e.HasOne(x => x.Tariff).WithMany(x => x.Bands).HasForeignKey(x => x.TariffId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.FromM3).HasPrecision(18, 3);
            e.Property(x => x.ToM3).HasPrecision(18, 3);
            e.Property(x => x.Rate).HasPrecision(18, 4);
        });

        // ---- pay
        T<Payment>(mb, "payments", "pay", e =>
        {
            e.HasIndex(x => x.ReceiptNo).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId);
            e.Property(x => x.ExchangeRate).HasPrecision(18, 6);
            e.Property(x => x.RowVersion).IsRowVersion();
            Check(e, "channel", V.Channel); Check(e, "status", V.PaymentStatus); Check(e, "currency_code", V.Currency);
            e.ToTable(t => t.HasCheckConstraint("ck_payments_cheque_status", "[cheque_status] IS NULL OR [cheque_status] IN ('pending','cleared','bounced')"));
        });
        T<PaymentAllocation>(mb, "payment_allocations", "pay", e =>
        {
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId);
            e.HasOne<Bill>().WithMany().HasForeignKey(x => x.BillId);
        });
        T<MpesaTransaction>(mb, "mpesa_transactions", "pay", e =>
        {
            e.HasIndex(x => x.TransId).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.MatchedTenantId);
            e.HasOne<Unit>().WithMany().HasForeignKey(x => x.MatchedUnitId);
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId);
            Check(e, "match_status", V.MpesaMatch); Json(e, "raw");
        });
        T<BankStatementUpload>(mb, "bank_statement_uploads", "pay", _ => { });
        T<BankStatementLine>(mb, "bank_statement_lines", "pay", e =>
        {
            e.HasOne<BankStatementUpload>().WithMany().HasForeignKey(x => x.UploadId);
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId);
            Check(e, "match_status", V.BankMatch);
        });

        // ---- ops
        T<Vendor>(mb, "vendors", "ops", _ => { });
        T<MaintenanceRequest>(mb, "maintenance_requests", "ops", e =>
        {
            e.HasIndex(x => x.RequestNo).IsUnique();
            e.HasOne<Unit>().WithMany().HasForeignKey(x => x.UnitId);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            e.HasOne<Vendor>().WithMany().HasForeignKey(x => x.VendorId);
            e.HasOne<Bill>().WithMany().HasForeignKey(x => x.BillId);
            Check(e, "category", V.MaintCategory); Check(e, "priority", V.Priority); Check(e, "status", V.MaintStatus); Check(e, "charge_to", V.ChargeTo);
        });
        T<MaintenancePhoto>(mb, "maintenance_photos", "ops", e =>
            e.HasOne<MaintenanceRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade));
        T<PreventiveTask>(mb, "preventive_tasks", "ops", e =>
        {
            e.HasOne<Property>().WithMany().HasForeignKey(x => x.PropertyId);
            e.HasOne<Vendor>().WithMany().HasForeignKey(x => x.AssignedVendorId);
        });
        T<Notice>(mb, "notices", "ops", e =>
        {
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            e.HasOne<Property>().WithMany().HasForeignKey(x => x.PropertyId);
            Check(e, "channel", V.NoticeChannel); Check(e, "status", V.NoticeStatus);
        });
        T<NotificationTemplate>(mb, "notification_templates", "ops", e =>
        {
            e.HasIndex(x => new { x.Code, x.Channel }).IsUnique();
            Check(e, "channel", V.NoticeChannel);
        });

        // ---- app
        T<BcSyncQueueItem>(mb, "bc_sync_queue", "app", e =>
        {
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasOne<BcSyncQueueItem>().WithMany().HasForeignKey(x => x.DependsOnId);
            Check(e, "entity_type", V.SyncEntity); Check(e, "status", V.SyncStatus); Json(e, "payload");
        });
        mb.Entity<NumberSeries>(e => { e.ToTable("number_series", "app"); e.HasKey(x => new { x.Code, x.Period }); });
        mb.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_log", "app", t =>
            {
                t.HasCheckConstraint("ck_audit_log_old_json", "[old_values] IS NULL OR ISJSON([old_values]) = 1");
                t.HasCheckConstraint("ck_audit_log_new_json", "[new_values] IS NULL OR ISJSON([new_values]) = 1");
            });
            e.HasIndex(x => new { x.TableName, x.RecordId });
        });
        T<UserPropertyAssignment>(mb, "user_property_assignments", "app", e =>
        {
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Landlord>().WithMany().HasForeignKey(x => x.LandlordId);
            e.HasOne<Property>().WithMany().HasForeignKey(x => x.PropertyId);
            e.HasIndex(x => x.UserId);
            e.ToTable(t => t.HasCheckConstraint("ck_upa_target", "[landlord_id] IS NOT NULL OR [property_id] IS NOT NULL"));
        });
        mb.Entity<BcPingLog>().ToTable("bc_ping_log", "app");

        // Nothing is deleted through a relationship unless configured above as a child table.
        foreach (var fk in mb.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            if (fk.DeleteBehavior == DeleteBehavior.Cascade && !CascadeAllowed(fk.DeclaringEntityType.ClrType))
                fk.DeleteBehavior = DeleteBehavior.Restrict;
    }

    static readonly HashSet<Type> CascadeChildren =
    [
        typeof(Block), typeof(UnitCharge), typeof(Meter), typeof(TenantDocument), typeof(LeaseCharge),
        typeof(RentEscalationRule), typeof(RentSchedule), typeof(BillLine), typeof(WaterTariffBand),
        typeof(MaintenancePhoto), typeof(UserPropertyAssignment),
        typeof(IdentityUserRole<string>), typeof(IdentityUserClaim<string>), typeof(IdentityUserLogin<string>),
        typeof(IdentityUserToken<string>), typeof(IdentityRoleClaim<string>)
    ];
    static bool CascadeAllowed(Type t) => CascadeChildren.Contains(t);

    static void T<TE>(ModelBuilder mb, string table, string schema, Action<EntityTypeBuilder<TE>> cfg) where TE : Entity
    {
        var e = mb.Entity<TE>();
        e.ToTable(table, schema);
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd()
            .HasValueGenerator<SequentialGuidValueGenerator>();
        Audited(e);
        cfg(e);
    }

    static EntityTypeBuilder<TE> Audited<TE>(EntityTypeBuilder<TE> e) where TE : class, IAudited
    {
        e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        e.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        e.Property(x => x.CreatedBy).HasMaxLength(450);
        e.Property(x => x.UpdatedBy).HasMaxLength(450);
        return e;
    }

    static void Check<TE>(EntityTypeBuilder<TE> e, string column, string[] values) where TE : class
    {
        var table = e.Metadata.GetTableName() ?? e.Metadata.ClrType.Name;
        var list = string.Join(",", values.Select(v => $"'{v}'"));
        e.ToTable(t => t.HasCheckConstraint($"ck_{table}_{column}", $"[{column}] IN ({list})"));
    }

    static void Json<TE>(EntityTypeBuilder<TE> e, string column) where TE : class
    {
        var table = e.Metadata.GetTableName() ?? e.Metadata.ClrType.Name;
        e.ToTable(t => t.HasCheckConstraint($"ck_{table}_{column}_json", $"ISJSON([{column}]) = 1"));
    }
}
