using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NavEstate.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenancy");

            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.EnsureSchema(
                name: "pay");

            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    table_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    record_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    old_values = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    new_values = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    user_id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.CheckConstraint("ck_audit_log_new_json", "[new_values] IS NULL OR ISJSON([new_values]) = 1");
                    table.CheckConstraint("ck_audit_log_old_json", "[old_values] IS NULL OR ISJSON([old_values]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_uploads",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    file_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    bank_account = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    uploaded_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    row_count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_statement_uploads", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bc_ping_log",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false),
                    ok = table.Column<bool>(type: "bit", nullable: false),
                    message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    duration_ms = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bc_ping_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bc_sync_queue",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    entity_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    function_name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    external_ref = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    last_error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    bc_document_no = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: true),
                    depends_on_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bc_sync_queue", x => x.id);
                    table.CheckConstraint("ck_bc_sync_queue_entity_type", "[entity_type] IN ('tenant','bill','credit_note','payment','reverse_payment','deposit','transfer')");
                    table.CheckConstraint("ck_bc_sync_queue_payload_json", "ISJSON([payload]) = 1");
                    table.CheckConstraint("ck_bc_sync_queue_status", "[status] IN ('pending','processing','success','failed')");
                    table.ForeignKey(
                        name: "fk_bc_sync_queue_bc_sync_queue_depends_on_id",
                        column: x => x.depends_on_id,
                        principalSchema: "app",
                        principalTable: "bc_sync_queue",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "charge_codes",
                schema: "tenancy",
                columns: table => new
                {
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    is_rent = table.Column<bool>(type: "bit", nullable: false),
                    allocation_priority = table.Column<int>(type: "int", nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_charge_codes", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "currencies",
                schema: "core",
                columns: table => new
                {
                    code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    bc_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currencies", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "landlords",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    kra_pin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    bank_details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    commission_percent = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    bc_vendor_no = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_landlords", x => x.id);
                    table.CheckConstraint("ck_landlords_type", "[type] IN ('individual','company')");
                });

            migrationBuilder.CreateTable(
                name: "notification_templates",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_templates", x => x.id);
                    table.CheckConstraint("ck_notification_templates_channel", "[channel] IN ('sms','email','portal')");
                });

            migrationBuilder.CreateTable(
                name: "number_series",
                schema: "app",
                columns: table => new
                {
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    period = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    last_no = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_series", x => new { x.code, x.period });
                });

            migrationBuilder.CreateTable(
                name: "payment_channels",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    bc_channel_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    account_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    currency_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_channels", x => x.id);
                    table.CheckConstraint("ck_payment_channels_bc_channel_code", "[bc_channel_code] IN ('MPESA','BANK','CHEQUE','CASH')");
                    table.CheckConstraint("ck_payment_channels_currency_code", "[currency_code] IN ('KES','USD')");
                });

            migrationBuilder.CreateTable(
                name: "penalty_rules",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    basis = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    cap_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    cap_value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    grace_days = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_penalty_rules", x => x.id);
                    table.CheckConstraint("ck_penalty_rules_basis", "[basis] IN ('fixed','percent_overdue_rent','percent_overdue_balance')");
                    table.CheckConstraint("ck_penalty_rules_cap_type", "[cap_type] IN ('none','amount','percent_of_rent')");
                    table.CheckConstraint("ck_penalty_rules_frequency", "[frequency] IN ('once','monthly')");
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "core",
                columns: table => new
                {
                    key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.key);
                    table.CheckConstraint("ck_settings_value_json", "ISJSON([value]) = 1 OR ISJSON(CONCAT('[', [value], ']')) = 1");
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    tenant_no = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    id_or_passport_no = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    kra_pin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    alt_phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    next_of_kin_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    next_of_kin_phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    company_reg_no = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    contact_person = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    blacklisted = table.Column<bool>(type: "bit", nullable: false),
                    blacklist_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    portal_user_id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    bc_synced_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                    table.CheckConstraint("ck_tenants_type", "[type] IN ('individual','company')");
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    landlord_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    user_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    security_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    phone_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "bit", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: true),
                    lockout_enabled = table.Column<bool>(type: "bit", nullable: false),
                    access_failed_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendors",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    service_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    kra_pin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    currency_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    kes_per_unit = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rates", x => x.id);
                    table.ForeignKey(
                        name: "fk_exchange_rates_currencies_currency_code",
                        column: x => x.currency_code,
                        principalSchema: "core",
                        principalTable: "currencies",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "properties",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    landlord_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    county = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    bulk_water_meter_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    default_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_properties", x => x.id);
                    table.CheckConstraint("ck_properties_default_currency", "[default_currency] IN ('KES','USD')");
                    table.CheckConstraint("ck_properties_type", "[type] IN ('residential','commercial','mixed')");
                    table.ForeignKey(
                        name: "fk_properties_landlords_landlord_id",
                        column: x => x.landlord_id,
                        principalSchema: "core",
                        principalTable: "landlords",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    role_id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    claim_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    claim_value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_claims_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "app",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_documents",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doc_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    file_path = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_documents", x => x.id);
                    table.CheckConstraint("ck_tenant_documents_doc_type", "[doc_type] IN ('id','kra_pin','lease','guarantor','other')");
                    table.ForeignKey(
                        name: "fk_tenant_documents_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    claim_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    claim_value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                schema: "app",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    provider_display_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    user_id = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    role_id = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "app",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    login_provider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "billing_runs",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    run_no = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    period = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    property_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    approved_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_runs", x => x.id);
                    table.CheckConstraint("ck_billing_runs_status", "[status] IN ('draft','approved','posted','partially_posted')");
                    table.ForeignKey(
                        name: "fk_billing_runs_properties_property_id",
                        column: x => x.property_id,
                        principalSchema: "core",
                        principalTable: "properties",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "blocks",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    property_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blocks", x => x.id);
                    table.ForeignKey(
                        name: "fk_blocks_properties_property_id",
                        column: x => x.property_id,
                        principalSchema: "core",
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notices",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    property_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notices", x => x.id);
                    table.CheckConstraint("ck_notices_channel", "[channel] IN ('sms','email','portal')");
                    table.CheckConstraint("ck_notices_status", "[status] IN ('queued','sent','failed')");
                    table.ForeignKey(
                        name: "fk_notices_properties_property_id",
                        column: x => x.property_id,
                        principalSchema: "core",
                        principalTable: "properties",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_notices_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "preventive_tasks",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    property_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    every_months = table.Column<int>(type: "int", nullable: false),
                    next_due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    assigned_vendor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preventive_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_preventive_tasks_properties_property_id",
                        column: x => x.property_id,
                        principalSchema: "core",
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_preventive_tasks_vendors_assigned_vendor_id",
                        column: x => x.assigned_vendor_id,
                        principalSchema: "ops",
                        principalTable: "vendors",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "user_property_assignments",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    user_id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    landlord_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    property_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_property_assignments", x => x.id);
                    table.CheckConstraint("ck_upa_target", "[landlord_id] IS NOT NULL OR [property_id] IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_user_property_assignments_landlords_landlord_id",
                        column: x => x.landlord_id,
                        principalSchema: "core",
                        principalTable: "landlords",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_user_property_assignments_properties_property_id",
                        column: x => x.property_id,
                        principalSchema: "core",
                        principalTable: "properties",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_user_property_assignments_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "water_tariffs",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    property_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    standing_charge = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_water_tariffs", x => x.id);
                    table.ForeignKey(
                        name: "fk_water_tariffs_properties_property_id",
                        column: x => x.property_id,
                        principalSchema: "core",
                        principalTable: "properties",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "units",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    property_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    block_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    unit_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    floor = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    area_sqft = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    default_rent = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    default_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_units", x => x.id);
                    table.CheckConstraint("ck_units_default_currency", "[default_currency] IN ('KES','USD')");
                    table.CheckConstraint("ck_units_status", "[status] IN ('vacant','reserved','occupied','maintenance','off_market')");
                    table.CheckConstraint("ck_units_unit_type", "[unit_type] IN ('bedsitter','1br','2br','3br','4br','maisonette','shop','office','warehouse','parking','kiosk')");
                    table.ForeignKey(
                        name: "fk_units_blocks_block_id",
                        column: x => x.block_id,
                        principalSchema: "core",
                        principalTable: "blocks",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_units_properties_property_id",
                        column: x => x.property_id,
                        principalSchema: "core",
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "water_tariff_bands",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    tariff_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_m3 = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    to_m3 = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_water_tariff_bands", x => x.id);
                    table.ForeignKey(
                        name: "fk_water_tariff_bands_water_tariffs_tariff_id",
                        column: x => x.tariff_id,
                        principalSchema: "billing",
                        principalTable: "water_tariffs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "leases",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    lease_no = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    currency_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    billing_day = table.Column<int>(type: "int", nullable: false),
                    frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    deposit_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    water_deposit_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    grace_days = table.Column<int>(type: "int", nullable: false),
                    penalty_rule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    vat_applicable = table.Column<bool>(type: "bit", nullable: false),
                    wht_agent = table.Column<bool>(type: "bit", nullable: false),
                    end_reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: true),
                    transferred_to_lease_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leases", x => x.id);
                    table.CheckConstraint("ck_leases_billing_day", "[billing_day] BETWEEN 1 AND 28");
                    table.CheckConstraint("ck_leases_currency_code", "[currency_code] IN ('KES','USD')");
                    table.CheckConstraint("ck_leases_frequency", "[frequency] IN ('monthly','quarterly','semi_annual','annual')");
                    table.CheckConstraint("ck_leases_status", "[status] IN ('draft','active','ended','transferred','terminated')");
                    table.ForeignKey(
                        name: "fk_leases_leases_transferred_to_lease_id",
                        column: x => x.transferred_to_lease_id,
                        principalSchema: "tenancy",
                        principalTable: "leases",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_leases_penalty_rules_penalty_rule_id",
                        column: x => x.penalty_rule_id,
                        principalSchema: "tenancy",
                        principalTable: "penalty_rules",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_leases_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leases_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "core",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meters",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    meter_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    serial_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    last_reading = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    last_reading_date = table.Column<DateOnly>(type: "date", nullable: true),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meters", x => x.id);
                    table.CheckConstraint("ck_meters_meter_type", "[meter_type] IN ('water','electricity')");
                    table.ForeignKey(
                        name: "fk_meters_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "core",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "unit_charges",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    charge_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_charges", x => x.id);
                    table.CheckConstraint("ck_unit_charges_currency_code", "[currency_code] IN ('KES','USD')");
                    table.CheckConstraint("ck_unit_charges_frequency", "[frequency] IN ('monthly','quarterly','semi_annual','annual','once')");
                    table.ForeignKey(
                        name: "fk_unit_charges_charge_codes_charge_code",
                        column: x => x.charge_code,
                        principalSchema: "tenancy",
                        principalTable: "charge_codes",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_unit_charges_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "core",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "advice_slips",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    slip_no = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lease_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    slip_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    slip_date = table.Column<DateOnly>(type: "date", nullable: false),
                    snapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    pdf_path = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_advice_slips", x => x.id);
                    table.CheckConstraint("ck_advice_slips_slip_type", "[slip_type] IN ('transfer','move_out')");
                    table.CheckConstraint("ck_advice_slips_snapshot_json", "ISJSON([snapshot]) = 1");
                    table.ForeignKey(
                        name: "fk_advice_slips_leases_lease_id",
                        column: x => x.lease_id,
                        principalSchema: "tenancy",
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_advice_slips_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bills",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    bill_no = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    lease_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    billing_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    bill_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    period = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    bill_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    total_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    vat_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    wht_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    amount_paid = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    amount_credited = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    balance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, computedColumnSql: "[total_amount] - [amount_paid] - [amount_credited]", stored: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    bc_document_no = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    bc_open_document_no = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    bc_sync_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bills", x => x.id);
                    table.CheckConstraint("ck_bills_bill_type", "[bill_type] IN ('rent','water','penalty','repair','deposit','other')");
                    table.CheckConstraint("ck_bills_currency_code", "[currency_code] IN ('KES','USD')");
                    table.CheckConstraint("ck_bills_status", "[status] IN ('draft','approved','posted','partially_paid','paid','cancelled')");
                    table.ForeignKey(
                        name: "fk_bills_billing_runs_billing_run_id",
                        column: x => x.billing_run_id,
                        principalSchema: "billing",
                        principalTable: "billing_runs",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_bills_leases_lease_id",
                        column: x => x.lease_id,
                        principalSchema: "tenancy",
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bills_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bills_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "core",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deposits",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lease_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deposit_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    currency_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    amount_held = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deposits", x => x.id);
                    table.CheckConstraint("ck_deposits_currency_code", "[currency_code] IN ('KES','USD')");
                    table.CheckConstraint("ck_deposits_deposit_type", "[deposit_type] IN ('rent','water')");
                    table.CheckConstraint("ck_deposits_status", "[status] IN ('held','applied','refunded','forfeited')");
                    table.ForeignKey(
                        name: "fk_deposits_leases_lease_id",
                        column: x => x.lease_id,
                        principalSchema: "tenancy",
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_deposits_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lease_charges",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    lease_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    charge_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lease_charges", x => x.id);
                    table.CheckConstraint("ck_lease_charges_currency_code", "[currency_code] IN ('KES','USD')");
                    table.CheckConstraint("ck_lease_charges_frequency", "[frequency] IN ('monthly','quarterly','semi_annual','annual','once')");
                    table.ForeignKey(
                        name: "fk_lease_charges_charge_codes_charge_code",
                        column: x => x.charge_code,
                        principalSchema: "tenancy",
                        principalTable: "charge_codes",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lease_charges_leases_lease_id",
                        column: x => x.lease_id,
                        principalSchema: "tenancy",
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    receipt_no = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lease_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    reference = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    payer_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    payer_phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    amount_in_lease_currency = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    cheque_bank = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    cheque_date = table.Column<DateOnly>(type: "date", nullable: true),
                    cheque_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    bc_document_no = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    bc_sync_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    received_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_channel", "[channel] IN ('mpesa','bank','cheque','cash')");
                    table.CheckConstraint("ck_payments_cheque_status", "[cheque_status] IS NULL OR [cheque_status] IN ('pending','cleared','bounced')");
                    table.CheckConstraint("ck_payments_currency_code", "[currency_code] IN ('KES','USD')");
                    table.CheckConstraint("ck_payments_status", "[status] IN ('unallocated','allocated','posted','reversed')");
                    table.ForeignKey(
                        name: "fk_payments_leases_lease_id",
                        column: x => x.lease_id,
                        principalSchema: "tenancy",
                        principalTable: "leases",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rent_escalation_rules",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    lease_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    every_months = table.Column<int>(type: "int", nullable: false),
                    compounding = table.Column<bool>(type: "bit", nullable: false),
                    notice_days = table.Column<int>(type: "int", nullable: false),
                    requires_approval = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rent_escalation_rules", x => x.id);
                    table.CheckConstraint("ck_rent_escalation_rules_type", "[type] IN ('fixed_percent','fixed_amount','stepped','market_review')");
                    table.ForeignKey(
                        name: "fk_rent_escalation_rules_leases_lease_id",
                        column: x => x.lease_id,
                        principalSchema: "tenancy",
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rent_schedule",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    lease_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    monthly_rent = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    change_label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    notice_sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: true),
                    approved_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rent_schedule", x => x.id);
                    table.CheckConstraint("ck_rent_schedule_status", "[status] IN ('completed','current','pending_approval','approved','scheduled')");
                    table.ForeignKey(
                        name: "fk_rent_schedule_leases_lease_id",
                        column: x => x.lease_id,
                        principalSchema: "tenancy",
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "water_readings",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    meter_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    period = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    previous_reading = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    current_reading = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    consumption = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, computedColumnSql: "[current_reading] - [previous_reading]", stored: true),
                    rate_per_m3 = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    standing_charge = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    photo_path = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    captured_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: true),
                    flag = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_water_readings", x => x.id);
                    table.CheckConstraint("ck_water_readings_flag", "[flag] IN ('none','high','zero','missing','lower')");
                    table.CheckConstraint("ck_water_readings_status", "[status] IN ('captured','verified','billed')");
                    table.ForeignKey(
                        name: "fk_water_readings_meters_meter_id",
                        column: x => x.meter_id,
                        principalSchema: "core",
                        principalTable: "meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_water_readings_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "core",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfers",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    transfer_no = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_lease_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    to_unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    to_lease_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    transfer_date = table.Column<DateOnly>(type: "date", nullable: false),
                    balance_option = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    deposit_option = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    advice_slip_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfers", x => x.id);
                    table.CheckConstraint("ck_transfers_balance_option", "[balance_option] IN ('carry_forward','settle_first')");
                    table.CheckConstraint("ck_transfers_deposit_option", "[deposit_option] IN ('move','move_and_top_up','refund_and_new')");
                    table.CheckConstraint("ck_transfers_status", "[status] IN ('draft','awaiting_approval','approved','completed','cancelled')");
                    table.ForeignKey(
                        name: "fk_transfers_advice_slips_advice_slip_id",
                        column: x => x.advice_slip_id,
                        principalSchema: "tenancy",
                        principalTable: "advice_slips",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_transfers_leases_from_lease_id",
                        column: x => x.from_lease_id,
                        principalSchema: "tenancy",
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transfers_leases_to_lease_id",
                        column: x => x.to_lease_id,
                        principalSchema: "tenancy",
                        principalTable: "leases",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_transfers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transfers_units_to_unit_id",
                        column: x => x.to_unit_id,
                        principalSchema: "core",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bill_lines",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    charge_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    vat_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bill_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_bill_lines_bills_bill_id",
                        column: x => x.bill_id,
                        principalSchema: "billing",
                        principalTable: "bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bill_lines_charge_codes_charge_code",
                        column: x => x.charge_code,
                        principalSchema: "tenancy",
                        principalTable: "charge_codes",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credit_notes",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    credit_no = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    approved_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    bc_document_no = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_notes", x => x.id);
                    table.CheckConstraint("ck_credit_notes_currency_code", "[currency_code] IN ('KES','USD')");
                    table.CheckConstraint("ck_credit_notes_status", "[status] IN ('draft','approved','posted')");
                    table.ForeignKey(
                        name: "fk_credit_notes_bills_bill_id",
                        column: x => x.bill_id,
                        principalSchema: "billing",
                        principalTable: "bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_credit_notes_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_requests",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    request_no = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    assigned_to = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    vendor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    target_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: true),
                    cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    charge_to = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_requests", x => x.id);
                    table.CheckConstraint("ck_maintenance_requests_category", "[category] IN ('plumbing','electrical','structural','painting','appliance','other')");
                    table.CheckConstraint("ck_maintenance_requests_charge_to", "[charge_to] IN ('tenant','landlord','none')");
                    table.CheckConstraint("ck_maintenance_requests_priority", "[priority] IN ('low','medium','high','emergency')");
                    table.CheckConstraint("ck_maintenance_requests_status", "[status] IN ('logged','assigned','in_progress','completed','closed')");
                    table.ForeignKey(
                        name: "fk_maintenance_requests_bills_bill_id",
                        column: x => x.bill_id,
                        principalSchema: "billing",
                        principalTable: "bills",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_maintenance_requests_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_maintenance_requests_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "core",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_requests_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalSchema: "ops",
                        principalTable: "vendors",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "deposit_movements",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    deposit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    movement = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    reference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    approved_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    bc_document_no = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deposit_movements", x => x.id);
                    table.CheckConstraint("ck_deposit_movements_movement", "[movement] IN ('receive','refund','apply_to_bills','forfeit','transfer')");
                    table.ForeignKey(
                        name: "fk_deposit_movements_deposits_deposit_id",
                        column: x => x.deposit_id,
                        principalSchema: "tenancy",
                        principalTable: "deposits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_lines",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    upload_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    txn_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    narration = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    match_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    payment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_statement_lines", x => x.id);
                    table.CheckConstraint("ck_bank_statement_lines_match_status", "[match_status] IN ('matched','fuzzy','unmatched','ignored')");
                    table.ForeignKey(
                        name: "fk_bank_statement_lines_bank_statement_uploads_upload_id",
                        column: x => x.upload_id,
                        principalSchema: "pay",
                        principalTable: "bank_statement_uploads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bank_statement_lines_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "pay",
                        principalTable: "payments",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "mpesa_transactions",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    trans_id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    trans_time = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    msisdn = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    first_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    bill_ref_number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    business_short_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    raw = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    matched_tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    matched_unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    match_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    payment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mpesa_transactions", x => x.id);
                    table.CheckConstraint("ck_mpesa_transactions_match_status", "[match_status] IN ('matched','fuzzy','unmatched','ignored')");
                    table.CheckConstraint("ck_mpesa_transactions_raw_json", "ISJSON([raw]) = 1");
                    table.ForeignKey(
                        name: "fk_mpesa_transactions_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "pay",
                        principalTable: "payments",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_mpesa_transactions_tenants_matched_tenant_id",
                        column: x => x.matched_tenant_id,
                        principalSchema: "tenancy",
                        principalTable: "tenants",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_mpesa_transactions_units_matched_unit_id",
                        column: x => x.matched_unit_id,
                        principalSchema: "core",
                        principalTable: "units",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    payment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocations", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_allocations_bills_bill_id",
                        column: x => x.bill_id,
                        principalSchema: "billing",
                        principalTable: "bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_allocations_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "pay",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_photos",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    file_path = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    taken_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_maintenance_photos_maintenance_requests_request_id",
                        column: x => x.request_id,
                        principalSchema: "ops",
                        principalTable: "maintenance_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_advice_slips_lease_id",
                schema: "tenancy",
                table: "advice_slips",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_advice_slips_slip_no",
                schema: "tenancy",
                table: "advice_slips",
                column: "slip_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_advice_slips_tenant_id",
                schema: "tenancy",
                table: "advice_slips",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_table_name_record_id",
                schema: "app",
                table: "audit_log",
                columns: new[] { "table_name", "record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_lines_payment_id",
                schema: "pay",
                table: "bank_statement_lines",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_lines_upload_id",
                schema: "pay",
                table: "bank_statement_lines",
                column: "upload_id");

            migrationBuilder.CreateIndex(
                name: "ix_bc_sync_queue_depends_on_id",
                schema: "app",
                table: "bc_sync_queue",
                column: "depends_on_id");

            migrationBuilder.CreateIndex(
                name: "ix_bc_sync_queue_entity_type_entity_id",
                schema: "app",
                table: "bc_sync_queue",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bc_sync_queue_status_next_attempt_at",
                schema: "app",
                table: "bc_sync_queue",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bill_lines_bill_id",
                schema: "billing",
                table: "bill_lines",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_bill_lines_charge_code",
                schema: "billing",
                table: "bill_lines",
                column: "charge_code");

            migrationBuilder.CreateIndex(
                name: "ix_billing_runs_property_id",
                schema: "billing",
                table: "billing_runs",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_billing_runs_run_no",
                schema: "billing",
                table: "billing_runs",
                column: "run_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bills_bill_no",
                schema: "billing",
                table: "bills",
                column: "bill_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bills_billing_run_id",
                schema: "billing",
                table: "bills",
                column: "billing_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_bills_lease_id",
                schema: "billing",
                table: "bills",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_bills_tenant_id_status",
                schema: "billing",
                table: "bills",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_bills_unit_id",
                schema: "billing",
                table: "bills",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_blocks_property_id_name",
                schema: "core",
                table: "blocks",
                columns: new[] { "property_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_bill_id",
                schema: "billing",
                table: "credit_notes",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_credit_no",
                schema: "billing",
                table: "credit_notes",
                column: "credit_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_tenant_id",
                schema: "billing",
                table: "credit_notes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_deposit_movements_deposit_id",
                schema: "tenancy",
                table: "deposit_movements",
                column: "deposit_id");

            migrationBuilder.CreateIndex(
                name: "ix_deposits_lease_id",
                schema: "tenancy",
                table: "deposits",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_deposits_tenant_id",
                schema: "tenancy",
                table: "deposits",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_currency_code_rate_date",
                schema: "core",
                table: "exchange_rates",
                columns: new[] { "currency_code", "rate_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lease_charges_charge_code",
                schema: "tenancy",
                table: "lease_charges",
                column: "charge_code");

            migrationBuilder.CreateIndex(
                name: "ix_lease_charges_lease_id",
                schema: "tenancy",
                table: "lease_charges",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_leases_lease_no",
                schema: "tenancy",
                table: "leases",
                column: "lease_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leases_penalty_rule_id",
                schema: "tenancy",
                table: "leases",
                column: "penalty_rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_leases_tenant_id",
                schema: "tenancy",
                table: "leases",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_leases_transferred_to_lease_id",
                schema: "tenancy",
                table: "leases",
                column: "transferred_to_lease_id");

            migrationBuilder.CreateIndex(
                name: "ux_leases_one_active_per_unit",
                schema: "tenancy",
                table: "leases",
                column: "unit_id",
                unique: true,
                filter: "[status] = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_photos_request_id",
                schema: "ops",
                table: "maintenance_photos",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_requests_bill_id",
                schema: "ops",
                table: "maintenance_requests",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_requests_request_no",
                schema: "ops",
                table: "maintenance_requests",
                column: "request_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_requests_tenant_id",
                schema: "ops",
                table: "maintenance_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_requests_unit_id",
                schema: "ops",
                table: "maintenance_requests",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_requests_vendor_id",
                schema: "ops",
                table: "maintenance_requests",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_meters_unit_id",
                schema: "core",
                table: "meters",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_mpesa_transactions_matched_tenant_id",
                schema: "pay",
                table: "mpesa_transactions",
                column: "matched_tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_mpesa_transactions_matched_unit_id",
                schema: "pay",
                table: "mpesa_transactions",
                column: "matched_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_mpesa_transactions_payment_id",
                schema: "pay",
                table: "mpesa_transactions",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_mpesa_transactions_trans_id",
                schema: "pay",
                table: "mpesa_transactions",
                column: "trans_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notices_property_id",
                schema: "ops",
                table: "notices",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_notices_tenant_id",
                schema: "ops",
                table: "notices",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_templates_code_channel",
                schema: "ops",
                table: "notification_templates",
                columns: new[] { "code", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_bill_id",
                schema: "pay",
                table: "payment_allocations",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_payment_id",
                schema: "pay",
                table: "payment_allocations",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_channels_code",
                schema: "core",
                table: "payment_channels",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_lease_id",
                schema: "pay",
                table: "payments",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_receipt_no",
                schema: "pay",
                table: "payments",
                column: "receipt_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id",
                schema: "pay",
                table: "payments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_preventive_tasks_assigned_vendor_id",
                schema: "ops",
                table: "preventive_tasks",
                column: "assigned_vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_preventive_tasks_property_id",
                schema: "ops",
                table: "preventive_tasks",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_properties_code",
                schema: "core",
                table: "properties",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_properties_landlord_id",
                schema: "core",
                table: "properties",
                column: "landlord_id");

            migrationBuilder.CreateIndex(
                name: "ix_rent_escalation_rules_lease_id",
                schema: "tenancy",
                table: "rent_escalation_rules",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_rent_schedule_lease_id",
                schema: "tenancy",
                table: "rent_schedule",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id",
                schema: "app",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "app",
                table: "roles",
                column: "normalized_name",
                unique: true,
                filter: "[normalized_name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_documents_tenant_id",
                schema: "tenancy",
                table: "tenant_documents",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_full_name",
                schema: "tenancy",
                table: "tenants",
                column: "full_name");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_phone",
                schema: "tenancy",
                table: "tenants",
                column: "phone");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_tenant_no",
                schema: "tenancy",
                table: "tenants",
                column: "tenant_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfers_advice_slip_id",
                schema: "tenancy",
                table: "transfers",
                column: "advice_slip_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfers_from_lease_id",
                schema: "tenancy",
                table: "transfers",
                column: "from_lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfers_tenant_id",
                schema: "tenancy",
                table: "transfers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfers_to_lease_id",
                schema: "tenancy",
                table: "transfers",
                column: "to_lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfers_to_unit_id",
                schema: "tenancy",
                table: "transfers",
                column: "to_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfers_transfer_no",
                schema: "tenancy",
                table: "transfers",
                column: "transfer_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unit_charges_charge_code",
                schema: "core",
                table: "unit_charges",
                column: "charge_code");

            migrationBuilder.CreateIndex(
                name: "ix_unit_charges_unit_id",
                schema: "core",
                table: "unit_charges",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_units_block_id",
                schema: "core",
                table: "units",
                column: "block_id");

            migrationBuilder.CreateIndex(
                name: "ix_units_property_id_code",
                schema: "core",
                table: "units",
                columns: new[] { "property_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                schema: "app",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                schema: "app",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_property_assignments_landlord_id",
                schema: "app",
                table: "user_property_assignments",
                column: "landlord_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_property_assignments_property_id",
                schema: "app",
                table: "user_property_assignments",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_property_assignments_user_id",
                schema: "app",
                table: "user_property_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                schema: "app",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "app",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "app",
                table: "users",
                column: "normalized_user_name",
                unique: true,
                filter: "[normalized_user_name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_water_readings_meter_id_period",
                schema: "billing",
                table: "water_readings",
                columns: new[] { "meter_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_water_readings_unit_id",
                schema: "billing",
                table: "water_readings",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_water_tariff_bands_tariff_id",
                schema: "billing",
                table: "water_tariff_bands",
                column: "tariff_id");

            migrationBuilder.CreateIndex(
                name: "ix_water_tariffs_property_id",
                schema: "billing",
                table: "water_tariffs",
                column: "property_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "app");

            migrationBuilder.DropTable(
                name: "bank_statement_lines",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "bc_ping_log",
                schema: "app");

            migrationBuilder.DropTable(
                name: "bc_sync_queue",
                schema: "app");

            migrationBuilder.DropTable(
                name: "bill_lines",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "credit_notes",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "deposit_movements",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "exchange_rates",
                schema: "core");

            migrationBuilder.DropTable(
                name: "lease_charges",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "maintenance_photos",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "mpesa_transactions",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "notices",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "notification_templates",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "number_series",
                schema: "app");

            migrationBuilder.DropTable(
                name: "payment_allocations",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "payment_channels",
                schema: "core");

            migrationBuilder.DropTable(
                name: "preventive_tasks",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "rent_escalation_rules",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "rent_schedule",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "role_claims",
                schema: "app");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "core");

            migrationBuilder.DropTable(
                name: "tenant_documents",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "transfers",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "unit_charges",
                schema: "core");

            migrationBuilder.DropTable(
                name: "user_claims",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_logins",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_property_assignments",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "app");

            migrationBuilder.DropTable(
                name: "water_readings",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "water_tariff_bands",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "bank_statement_uploads",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "deposits",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "currencies",
                schema: "core");

            migrationBuilder.DropTable(
                name: "maintenance_requests",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "advice_slips",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "charge_codes",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "app");

            migrationBuilder.DropTable(
                name: "users",
                schema: "app");

            migrationBuilder.DropTable(
                name: "meters",
                schema: "core");

            migrationBuilder.DropTable(
                name: "water_tariffs",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "bills",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "vendors",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "billing_runs",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "leases",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "penalty_rules",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "units",
                schema: "core");

            migrationBuilder.DropTable(
                name: "blocks",
                schema: "core");

            migrationBuilder.DropTable(
                name: "properties",
                schema: "core");

            migrationBuilder.DropTable(
                name: "landlords",
                schema: "core");
        }
    }
}
